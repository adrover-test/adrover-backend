# Adrover – Advertisement Scheduling & Playback API

> Status: **implementation notes and proposed API design**. The API, outbox and
> Function workers are implemented but require the SQL migration, configuration and
> deployment described in [the Functions README](src\Adrover.Functions\README.md).
> Sections 3.5, 7.1, 7.2, 7.4 and 7.5 describe proposed endpoints, not implemented
> routes. The allocation is an estimate: Intelisa does not provide exact play caps,
> real-time playback callbacks, or a guarantee of contractual delivery.

All REST routes are relative to the server origin (for example
`https://<api-host>/api/...`) and require `Authorization: Bearer <access token>`.
Errors use the existing envelope produced by `ExceptionHandlingMiddleware`:

```json
{
  "success": false,
  "error": { "code": "ERROR_CODE", "message": "Human-readable message", "details": null },
  "timestamp": "2026-11-24T00:30:00Z"
}
```

All campaign dates are **India Standard Time (IST, UTC+05:30)** calendar dates in
`yyyy-MM-dd` format, which matches Intelisa. "Today" always means today in IST.

---

## 1. End-to-end flow

```
Client uploads video ──┐
					   ├─► both done ─► Service Bus "ad-content-processing"
Payment captured  ─────┘                    │
											▼
					   Function: ContentProcessor (Service Bus trigger)
						 1. Intelisa upload creative  → creative id (Content ID)
						 2. Poll creative status until "ready"
						 3. Save Content ID, mark ReadyForScheduling
											│
											▼
					   Function: DailyScheduler (timer, every morning IST)
						 1. Pull yesterday's proof of play (Creative Detail Report)
						 2. Record first playback → status Started (edit lock)
						 3. Compute today's plays per ad and per screen
						 4. Reserve company / mandatory inventory first
						 5. Send schedules to screens (Intelisa Schedule content)
```

---

## 2. Advertisement lifecycle

### 2.1 Status values

The existing `AdvertisementStatus` values stay the same. One value is added:

| Value | Name | Meaning |
| --- | --- | --- |
| 1 | `PendingApproval` | Submitted, waiting for admin review |
| 2 | `Approved` | Approved by admin |
| 3 | `Active` | Paid, approved and eligible for scheduling |
| 4 | `Completed` | Contracted plays delivered or end date passed |
| 5 | `Rejected` | Rejected by admin |
| 6 | `Cancelled` | Cancelled |
| 7 | `Draft` | Registration draft, not yet paid |
| **8** | **`Started`** | **First playback recorded; campaign is locked for editing** |

### 2.2 Content (setup) state

Content processing is tracked separately from `status` in `contentState`:

| `contentState` | Meaning |
| --- | --- |
| `NotSubmitted` | Video or payment is still missing |
| `Queued` | An outbox entry exists; it may still be awaiting Service Bus publication |
| `Uploading` | Intelisa upload is in progress |
| `UploadUnconfirmed` | Upload may have succeeded; manual reconciliation is required |
| `Processing` | Uploaded to Intelisa and waiting for the creative to be `ready` |
| `Ready` | Content ID saved; `readyForScheduling = true` |
| `Failed` | Intelisa rejected the file or content processing could not be completed |

### 2.3 New advertisement fields

The following fields are added to `Advertisement` and returned in `AdvertisementDto`:

| Field | Type | Description |
| --- | --- | --- |
| `contentId` | string? | Intelisa creative ID (`creative.id` from Upload creative) |
| `contentState` | string | See 2.2 |
| `readyForScheduling` | bool | `true` when payment is complete and the Content ID is ready |
| `firstPlayedAt` | datetime? (UTC) | Exact first playback only if a trustworthy timestamp is available |
| `firstPlaybackDate` | date? | Earliest reported day with playback, without a fabricated time |
| `lastReportedDay` | date? | Latest date covered by trusted provider play data |
| `isEditable` | bool | `false` after checkout or observed playback |
| `actualPlays` | long | Reported plays through the latest trusted report day |
| `setupValidationMessage` | string? | Exact overdue setup message, when applicable |

`targetPlays` is the **total contracted plays**.

Database migration: `database\migrations\20261125-ad-scheduling.sql` (additive and
safe to run more than once). It adds the columns above plus the tables in section 8.

---

## 3. Video processing workflow (Service Bus)

### 3.1 Trigger

A message is published **once** when **both** of these are true, whichever happens
last:

1. A video is attached (`POST /api/Advertisements/{id}/upload-video`), and
2. Payment is `Completed` (`POST /api/Payments/verify`, the order-status refresh, or
   the Razorpay webhook).

The intended design records an outbox row in the same database transaction that
completes the second condition. A separate dispatcher publishes it after commit.
Service Bus delivery can be repeated, so consumers must be idempotent; an outbox
alone does not guarantee exactly-once delivery.

### 3.2 Queue

| Setting | Value |
| --- | --- |
| Namespace setting | `ServiceBus:FullyQualifiedNamespace` (managed identity) or `ServiceBus:ConnectionString` |
| Queue | `ad-content-processing` |
| Duplicate detection | Enabled, 1-day window |
| Max delivery count | 10, then dead-letter |
| Lock duration | 5 minutes |

### 3.3 Message contract

`ContentType: application/json`, `MessageId: content-{advertisementId}-v{assetVersion}`
(duplicate detection uses this ID).

```json
{
  "messageType": "AdContentReady.v1",
  "advertisementId": "3f6c2b9e-6a1d-4b0f-9a55-0c1f2d3e4a5b",
  "assetVersion": 1,
  "videoBlobPath": "advertisements/ads/3f6c.../video_20261124093000.mp4",
  "paymentId": "8d1a...",
  "requestedAtUtc": "2026-11-24T04:00:00Z"
}
```

The message carries a **blob path, not a public URL or SAS token**. The consumer
reads the file with its own managed identity.

### 3.4 Consumer (`ContentProcessor` function)

1. Load the advertisement. Stop (and complete the message) if it is already `Ready`
   with the same `assetVersion`, or if it has been cancelled or rejected.
2. Log in to Intelisa (`POST /api/v1/auth/login`) and cache the token for 23 hours
   or until it gets a 401.
3. `POST /api/v1/creatives/upload` (multipart) with `file` and
   `adGroup = "<brand> - <ad id>"`. Omit optional validity dates so a later campaign
   reschedule does not invalidate the creative. Save `creative.id` as `contentId`
   and set `contentState = Processing` before polling. If the upload outcome is
   unknown, reconcile it before retrying: saving the ID after a successful response
   alone cannot prevent duplicates after a timeout or crash.
4. Poll `POST /api/v1/creatives/status` with `{ "creative_ids": ["<contentId>"] }`
   every 20 seconds for up to 10 minutes. If it is not ready yet, the message is
   re-scheduled with a 2-minute delay instead of blocking.
5. When the status is `ready`, set `contentState = Ready`, `readyForScheduling = true`,
   and `readyForSchedulingAt = now`.
6. Errors:
   - Intelisa 400 or 413 (bad file or too large): `contentState = Failed`, a safe
	 `contentError` is saved, and the message is completed (not retried).
   - 401: log in again once, then retry where safe.
   - Retry read-only operations on transient errors. A creative upload or schedule
	 write that times out may have succeeded; do not blindly repeat such a write.
	 Record the uncertain outcome and reconcile it before any retry.

### 3.5 Admin retry

**POST** `/api/Admin/advertisements/{id}/content/retry` (SuperAdmin)

Publishes the message again with a new `assetVersion`. Allowed only when
`contentState` is `Failed` and the ad has not started.

**Response 202:** `{ "advertisementId": "...", "contentState": "Queued" }`

---

## 4. Daily scheduling (Azure Function App)

### 4.1 Function App

| Item | Value |
| --- | --- |
| Project | `src\Adrover.Functions` (.NET isolated worker) |
| `DailyScheduler` | Timer trigger `0 30 0 * * *` (00:30 UTC = **06:00 IST**; Functions host must use UTC) |
| `ContentProcessor` | Service Bus trigger on `ad-content-processing` |
| Concurrency | The scheduler takes a SQL application lock (`sp_getapplock 'daily-scheduler'`), so only one run executes at a time |
| Idempotency | One `ScheduleRuns` row per IST date. A re-run for the same date only sends the screens that are still missing |

Required settings: `ConnectionStrings:DefaultConnection`, `Intelisa:BaseUrl`,
`Intelisa:UserName`, `Intelisa:Password`, `ServiceBus:*`, `AzureBlob:*`, and the
`Scheduling:*` settings in 4.4, including a valid `HouseAdContentId`. Store secrets
in Key Vault references, not in `appsettings.json`.

### 4.2 Eligible advertisements

An ad is included in today's run when **all** of these are true:

- `status` is `Active` or `Started`
- Payment is `Completed` (`paidAmount ≥ approvalAmount`)
- `readyForScheduling = true` and `contentId` is set
- `endDate ≥ today` and `startDate ≤ today` (future ads are kept and included from their start day)
- `remainingPlays > 0`

### 4.3 Step 1 – proof of play and first playback

For every eligible or started ad, the scheduler calls the Intelisa Creative Detail Report:

```
POST /api/v1/reports/creative-detail
{ "creative_id": "<contentId>", "start_date": "<startDate>", "end_date": "<yesterday>" }
```

- `actualPlays` sums `per_day.plays` only through
  `data_freshness.latest_day_with_play_data`. Later days are treated as
  **not yet reported**, not as zero, and are re-read the next day.
- Per-screen, per-day numbers (`screens[].daily_plays`) are stored in
  `AdDailyDelivery` for the shortfall report.
- **First playback:** if `totals.plays > 0`, record the first `per_day.date` with
  `plays > 0` as the first observed playback date and set `status = Started`. The
  report only provides a date, not the actual time; do not fabricate midnight as an
  exact `firstPlayedAt`. Populate that timestamp only from a trustworthy timestamped
  player event. Either kind of playback evidence locks editing (section 5).
- When `completedPlays ≥ targetPlays`, or `endDate < today`, set `status = Completed`.

### 4.4 Step 2 – inventory

Settings (`Scheduling` section):

| Setting | Default | Meaning |
| --- | --- | --- |
| `OperatingHoursPerDay` | `10` | Hours each screen plays per day |
| `OperatingWindowStartTime` | `08:00` | Start of the daily operating window in IST |
| `OperatingWindowEndTime` | `18:00` | End of the daily operating window in IST |
| `LoopSeconds` | `180` | Length of one rotation loop on a screen |
| `ReservedSharePercent` | `20` | Share of each loop kept for company or mandatory content |
| `ReservedContentIds` | `[]` | Intelisa creative IDs for company and mandatory content |
| `HouseAdContentId` | required | Intelisa creative ID used to fill commercial inventory not needed by contracted ads |
| `MinuteScheduling` | `false` | Allow minute-level schedule windows when supported by the account |
| `TargetAreaScreenIds` | required | Explicit area-to-screen mappings for paid campaigns |

For each screen that is `ON` or was seen in the last 24 hours (`POST /api/v1/screens`):

```
LoopsPerDay                 = floor(OperatingHoursPerDay × 3600 / LoopSeconds)
ReservedSecondsPerLoop      = ceil(LoopSeconds × ReservedSharePercent / 100)
AvailableCommercialSeconds = (LoopSeconds − ReservedSecondsPerLoop) × LoopsPerDay
PlayDurationSeconds(ad)     = max(5, ceil(ad video duration in seconds))
```

Capacity is measured in playback seconds, not raw plays, so longer creatives consume
more inventory. The duration-aware allocator converts each ad's daily target to
seconds before sharing capacity across campaigns and screens.

Company and mandatory content is scheduled **first** on every screen, using the
reserved seconds. Paid campaign allocations use only the remaining
`AvailableCommercialSeconds`. Any commercial time left after due contracted
allocations is filled with `HouseAdContentId`; this includes the time released when a
campaign has met its contracted play total. The house ad is a fallback, not an
additional contracted campaign.

### 4.5 Step 3 – play allocation algorithm

For each eligible ad *a*, allocate from its campaign start date, inclusive. No plays
are allocated before `startDate`. On the first campaign day, the baseline is the
contracted total divided across every campaign day, including both `startDate` and
`endDate`. On each later day, compensate using the actual reported plays through the
previous day:

```
CampaignDays(a)   = EndDate(a) − StartDate(a) + 1
CompletedPlays(a) = reported plays through yesterday
RemainingPlays(a) = max(0, TotalContractedPlays(a) − CompletedPlays(a))
RemainingDays(a)  = EndDate(a) − max(Today, StartDate(a)) + 1
DailyTarget(a)    = 0, if Today < StartDate or RemainingPlays = 0
                    ceil(RemainingPlays(a) / RemainingDays(a)), otherwise
```

On the start day, `RemainingDays = CampaignDays`, so the initial daily target is
`ceil(TotalContractedPlays / CampaignDays)`. Recalculate each morning from reported
actuals; missed or under-delivered plays are spread over the inclusive days still
available. On the final campaign day, the target is all remaining contracted plays.
If the target has already been fulfilled, it is zero and the campaign creative is
not scheduled again.

Screen distribution is proportional to the commercial time available on each
eligible screen. For an ad with a given play duration, compute how many whole plays
fit on each screen, assign each screen its proportional share, and distribute the
remaining whole plays by largest fractional remainder. Identical screens therefore
receive equal shares.

Capacity check (all ads together):

1. Compute each ad's demand in seconds: `DailyTarget × PlayDurationSeconds`.
2. If total demand exceeds total commercial seconds, scale each campaign's play target
   by the same capacity ratio, round down, then award remaining whole plays by largest
   fractional remainder when their duration fits. This keeps the distribution fair
   without exceeding screen capacity.
3. Split each scaled target across available screens, respecting each screen's
   remaining commercial seconds. Redistribute a screen's unusable remainder to other
   screens where the creative fits. Record any unmet contracted plays as `shortfall`;
   tomorrow's recalculation compensates for them.
4. Record the shortfall when the estimated time windows do not fit. No `atRisk`
   admin report is currently implemented.

**Example:** contracted 10,000 plays across a 30-day campaign (start and end dates
included), with 5 screens. On the first day, `ceil(10,000 / 30) = 334` plays are
targeted (distributed as 67, 67, 67, 67, and 66 across available screens). If 300
are reported for that day, the next day's target is `ceil(9,700 / 29) = 335`. The
allocation starts on the campaign start date; it is not divided across days before
the campaign begins.

### 4.6 Step 4 – send to screens and fill completed campaigns with house ads

Intelisa schedules content as a rotation; it does not take a play count or enforce a
per-creative play cap. For each daily screen plan, schedule the estimated time window
for contracted ads first, then schedule the house creative in the remaining
commercial window. When reports confirm an ad has reached `targetPlays`, stop
scheduling that campaign creative from the next scheduling run and replace its
allocated time window with `HouseAdContentId`. Do not add extra plays for that
completed campaign. Other contracted ads keep their own independently calculated
allocations.

Intelisa's proof-of-play data can arrive about two days late. Therefore the scheduler
switches that ad's allocated window to the house ad as soon as the contracted total
is confirmed by the freshest report, but cannot guarantee a switch at the exact
playback that fulfills the contract. Intelisa's documented API has no play-count cap
or real-time completion callback. If an immediate, exact handoff is required, the
screen player must support a play-count limit/callback; the scheduler must not claim
this guarantee without that capability.

The planned plays are converted into estimated **time windows** per screen:

```
WindowMinutes(a, screen) = ceil(PlannedPlays(a, screen) × LoopSeconds / 60)
```

The window is rounded up to whole hours for accounts without minute scheduling, and
then:

```
POST /api/v1/schedules
{
  "content": [{ "type": "creative", "id": "<contentId>" }],
  "screen_ids": ["<screenID>", "..."],          // up to 500, grouped by identical window
  "start_date": "<today>", "end_date": "<today>",
  "start_time": "HH:MM", "end_time": "HH:MM",   // 08:00–18:00 IST by default
  "mode": "add"
}
```

- `mode` is always `add`, so existing and company content stays scheduled.
- The scheduler first sends the request with `"dry_run": true`, then the real call.
- A write that ends in a timeout is **not** retried automatically, because it may
  have been applied. The screen allocation is marked `Unconfirmed` for admin review.
- Each call result is stored in `AdScreenAllocations.status`
  (`Planned`, `Sent`, `Unconfirmed`, `Failed`).

---

## 5. Edit restrictions after playback starts

When playback is observed (status `Started`, even if `firstPlayedAt` is unknown),
these requests are rejected with **422** and code `AD_PLAYBACK_STARTED`:

| Method | Route | What is protected |
| --- | --- | --- |
| PUT | `/api/Advertisements/{id}` | Campaign settings, dates, targeting, plays |
| PUT | `/api/Advertisements/drafts/{id}` | Plan, dates, targeting |
| POST | `/api/Advertisements/{id}/upload-video` | Creative asset |
| POST | `/api/Advertisements/{id}/upload-image` | Creative asset |
| PUT | `/api/Advertisements/{id}/reschedule` | Start date (section 7.3) |
| DELETE | `/api/Advertisements/{id}` | Campaign |

```json
{
  "success": false,
  "error": {
	"code": "AD_PLAYBACK_STARTED",
	"message": "This advertisement started playing on 2026-11-24 and can no longer be edited."
  }
}
```

The server checks recorded playback evidence under the campaign row lock. Intelisa
reports are delayed, so the system cannot immediately know about unreported playback;
`firstPlaybackDate` is the earliest observed report day, not an exact timestamp. The
frontend should use `isEditable` only to hide controls; the server is authoritative.

---

## 6. Incomplete setup validation

If `startDate < today` (IST) and **any** of these is incomplete:

- payment is not `Completed`
- `contentState` is not `Ready` (content processing)
- `contentId` is missing (Content ID generation)

then the ad is **not** scheduled. The API returns, and the frontend shows exactly:

> The scheduled start date has already passed and the advertisement setup is not yet complete. The advertisement can no longer be scheduled for the selected start date and must be rescheduled to begin from the next available day.

Where it applies:

| Place | Behaviour |
| --- | --- |
| `GET /api/Advertisements/{id}` and `/my-ads` | `validation` array contains `{ code: "AD_SETUP_INCOMPLETE_START_PASSED", message, nextAvailableStartDate }` |
| `GET /api/Advertisements/{id}/schedule-status` | Same object in `validation` |
| `POST /api/Payments/create-order`, `POST /api/Payments/verify` | Accepted (payment is never lost), but the response carries the same `validation` entry |
| `DailyScheduler` | The ad is skipped and logged with this code; no retroactive schedule is sent |
| `PUT /api/Advertisements/{id}/reschedule` with a start date before tomorrow IST | 422 with this code and message |

`nextAvailableStartDate` = tomorrow (IST). This gives content processing time to
finish before the first scheduled day.

---

## 7. REST endpoints

### 7.1 Get schedule status (client owner or SuperAdmin)

**GET** `/api/Advertisements/{id}/schedule-status`

**Response 200:**

```json
{
  "advertisementId": "3f6c2b9e-6a1d-4b0f-9a55-0c1f2d3e4a5b",
  "status": "Active",
  "paymentStatus": "Completed",
  "contentState": "Ready",
  "contentId": "6735a1f2c9e77b0012ab34cd",
  "readyForScheduling": true,
  "startDate": "2026-11-25",
  "endDate": "2026-12-24",
  "targetPlays": 10000,
  "completedPlays": 0,
  "remainingPlays": 10000,
  "remainingDays": 30,
  "todayTarget": 334,
  "operatingHoursPerDay": 10,
  "houseAdContentId": "6735a1f2c9e77b0012ab0002",
  "firstPlayedAt": null,
  "isEditable": true,
  "editLockReason": null,
  "lastReportedDay": "2026-11-23",
  "validation": []
}
```

Another client's campaign returns 404.

### 7.2 Get delivery plan

**GET** `/api/Advertisements/{id}/delivery-plan?from=2026-11-20&to=2026-11-26`

Client owner or SuperAdmin. The range can be at most 31 days.

```json
{
  "advertisementId": "3f6c...",
  "items": [
	{
	  "date": "2026-11-24",
	  "plannedPlays": 950,
	  "deliveredPlays": 900,
	  "shortfall": 50,
	  "reported": true,
	  "screens": [
		{ "screenId": "605892b8...", "screenName": "Lobby Screen", "plannedPlays": 190, "deliveredPlays": 182, "status": "Sent" }
	  ]
	}
  ]
}
```

`deliveredPlays` is `null` while the day is not yet reported by Intelisa (usually
about 2 days).

### 7.3 Reschedule (client owner, before playback)

**PUT** `/api/Advertisements/{id}/reschedule`

```json
{ "startDate": "2026-11-26" }
```

The original inclusive campaign duration, contracted plays, and price are preserved;
the end date moves by the same number of days as the start date. For a registration
campaign the saved draft progress date is updated as well. This route applies only
when setup was incomplete when the original start date passed.

| Rule | Error |
| --- | --- |
| Playback has started | 422 `AD_PLAYBACK_STARTED` |
| A provider schedule was submitted or its outcome is uncertain | 422 `AD_SCHEDULE_SUBMITTED` |
| `startDate` is before tomorrow IST | 422 `AD_SETUP_INCOMPLETE_START_PASSED` (exact message in section 6) |
| Campaign is not overdue due to incomplete setup, or is in an ineligible state | 422 business error |
| Campaign belongs to another client | 404 |

**Response 200:** the usual `{success, data, message}` envelope with the updated
advertisement DTO in `data`.

### 7.4 Admin – run the scheduler

**POST** `/api/Admin/scheduling/run` (SuperAdmin)

```json
{ "date": "2026-11-24", "dryRun": true }
```

`dryRun: true` calculates and returns the plan without calling Intelisa
`/schedules`. A real run for a date that already ran only sends the allocations that
are still missing.

**Response 202:** `{ "runId": "uuid", "date": "2026-11-24", "dryRun": true }`

### 7.5 Admin – list runs and plans

- **GET** `/api/Admin/scheduling/runs?pageNumber=1&pageSize=20`. Each run includes
  `runId`, `date`, `startedAt`, `finishedAt`, `status` (`Running`, `Succeeded`,
  `PartiallySucceeded`, `Failed`), `adsScheduled`, `screensUsed`,
  `capacityPlays`, `reservedPlays`, `plannedPlays`, `shortfallPlays`, and `atRiskAds`.
- **GET** `/api/Admin/scheduling/plans?date=2026-11-24` returns per-ad and per-screen
  allocations for that date, with `status`, `shortfall`, and `atRisk`.

### 7.6 Admin – reserved inventory

**GET / PUT** `/api/Admin/scheduling/reserved-inventory`

```json
{
  "reservedSharePercent": 20,
  "reservedContentIds": ["6735a1f2c9e77b0012ab0001"]
}
```

`reservedSharePercent` must be between 0 and 90. Changes apply from the next run.

---

## 8. Data tables

| Table | Key columns |
| --- | --- |
| `ContentOutbox` | `Id`, `AdvertisementId`, `AssetVersion`, `MessageId` (unique), `CreatedAt`, `PublishedAt` |
| `ScheduleRuns` | `Id`, `RunDate` (unique for non-dry runs), `Status`, counters from 7.5, `StartedAt`, `FinishedAt` |
| `AdScreenAllocations` | `RunId`, `AdvertisementId`, `ScreenId`, `PlayDate`, `PlannedPlays`, `StartTime`, `EndTime`, `Status`; unique (`AdvertisementId`, `ScreenId`, `PlayDate`) |
| `AdDailyDelivery` | `AdvertisementId`, `ScreenId`, `PlayDate`, `Plays`, `Minutes`, `ReportedAt`; unique (`AdvertisementId`, `ScreenId`, `PlayDate`) |

---

## 9. Error codes

| Code | HTTP | Meaning |
| --- | --- | --- |
| `AD_PLAYBACK_STARTED` | 422 | Edit blocked because playback has started |
| `AD_SETUP_INCOMPLETE_START_PASSED` | 422 | Start date passed before setup was complete (exact message in section 6) |
| `AD_CONTENT_NOT_READY` | 422 | An action needs a ready Content ID |
| `AD_CONTENT_RETRY_NOT_ALLOWED` | 422 | Content retry is only allowed when `contentState = Failed` |
| `SCHEDULER_RUN_IN_PROGRESS` | 409 | Another scheduler run holds the lock |
| `INTELISA_UNAVAILABLE` | 502 | Intelisa failed or timed out (no secrets or raw bodies are returned) |

---

## 10. Frontend notes (`adrover-demo`)

- Show `status = Started` as **Playing** with `firstPlayedAt`, and disable the edit,
  upload, and reschedule controls when `isEditable = false`.
- Show `contentState`: `Queued` and `Processing` as "Preparing your video", `Ready`
  as "Ready for scheduling", and `Failed` with `contentError`.
- When `validation` contains `AD_SETUP_INCOMPLETE_START_PASSED`, show its `message`
  exactly as returned and offer a reschedule starting at `nextAvailableStartDate`.
- On any 422 from an edit, show `error.message` and reload the schedule status.
