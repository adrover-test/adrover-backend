# Adrover Functions delivery workers

This .NET isolated Azure Functions v4 project provides:

- A 30-second outbox dispatcher publishing `AdContentReady.v1` messages.
- A Service Bus consumer that uploads a private video blob to Intelisa, persists its creative ID, polls status using delayed queue messages, and marks ready only after Intelisa reports `ready`.
- A daily IST scheduler that retrieves date-granular proof-of-play, allocates only to explicitly configured target-area screens, previews each schedule write, and records each write's outcome.

## Required configuration

Use Key Vault references or managed identity for secrets. Do not commit credentials.

- `AzureWebJobsStorage` (Functions host storage)
- `ConnectionStrings__DefaultConnection`
- `Intelisa__BaseUrl` (default `https://login.intelisa.in`), `Intelisa__UserName`, `Intelisa__Password`
- `ServiceBusConnection` (connection string) **or** `ServiceBusConnection__fullyQualifiedNamespace` (managed identity). The queue is `ad-content-processing`.
- `AzureBlob__ContainerName=advertisements` (matching the API upload container) and either `AzureBlob__ConnectionString` or `AzureBlob__ServiceUri` (managed identity)
- `Scheduling__HouseAdContentId`
- `Scheduling__TargetAreaScreenIds__<target-area-key>__0`, `__1`, ... for explicit TargetArea-to-Intelisa-screen mappings. Matching is case-insensitive on the area key; IDs must match `screenID` values returned by Intelisa. No mapping means the paid campaign is blocked, never broadcast to every screen.
- Optional operating-window (default 08:00–18:00 IST), loop and reserved-content settings. The daily timer is fixed at 00:30 UTC (06:00 IST); keep the Functions host time zone in UTC.

The queue trigger and storage account must be configured for managed identity where supported. Enable Service Bus duplicate detection for a one-day window, lock duration of five minutes, and maximum delivery count ten. The stable content message ID is `content-{advertisementId:D}-v{assetVersion}`.

## API transaction / outbox contract

The API transaction observing both conditions (video persisted and payment status `Completed`) inserts a `ContentOutbox` entity using:

```csharp
ContentOutbox.Create(advertisementId, assetVersion, privateBlobPath, paymentId, DateTime.UtcNow)
```

and set the advertisement `ContentState = "Queued"`, `ReadyForScheduling = false` in that same transaction. `assetVersion` must increase for each replacement/retry; `videoBlobPath` is a private container-relative path, not a public URL/SAS. `MessageId` and `(AdvertisementId, AssetVersion)` are unique. The outbox dispatcher publishes JSON `{ messageType, advertisementId, assetVersion, videoBlobPath, paymentId, requestedAtUtc }`, then records `PublishedAtUtc`. The row is intentionally insertable from whichever API flow (payment completion or upload) observes the second condition. Do not publish directly from a controller.

`PendingApproval`/`Approved` status transitions remain API/admin-owned. The Functions consumer only sets content fields; the scheduler includes only `Active` or `Started` ads, so a paid/ready campaign can wait for approval without being activated by a worker.

## Scheduling safety and provider limitations

`AdScreenAllocations` is the durable write ledger, keyed by run, screen, date, time window, and content ID. Campaign rows transition `Planned -> Submitting -> Sent|Failed|Unconfirmed`; a leftover `Submitting` becomes `Unconfirmed` on the next run. The reschedule guard blocks when a campaign row is `Submitting`, `Sent`, or `Unconfirmed`; `Failed` rows were rejected or failed preview and do not indicate an applied write. Before each paid write, the scheduler starts a SQL transaction, locks the corresponding advertisement row with `UPDLOCK, ROWLOCK`, revalidates the campaign and route, persists `Submitting`, then commits before contacting Intelisa. This coordinates with the API's row-locked reschedule transaction: whichever obtains the row lock first wins; a changed campaign is not dispatched. `Sent` and `Unconfirmed` mean a provider write may exist; Intelisa has no documented schedule removal/cancellation or play-count cap, so do not auto-repeat uncertain writes or claim exact play counts.



Intelisa reports are daily aggregates and may lag roughly two days. `LastReportedDay` is the newest trusted date, and `ActualPlays` sums only `per_day` entries through that date. Screen-level daily play counts are stored with `Minutes = NULL` because the guide does not provide per-screen/per-day minutes. When a report is behind, prior `Sent`/`Unconfirmed` allocations are provisionally reserved against the remaining target; these are never added to `ActualPlays` or used to mark a contract complete. If an unreported day has no prior allocation record, the campaign fails closed for that run. `FirstPlaybackDate` records the earliest report day and remains populated after completion; date-granular reports do not set `FirstPlayedAt`, which remains null unless an exact provider timestamp exists.


Paid allocation requires an exact configured `TargetAreaScreenIds` entry. The existing `DailyPlayAllocator` is applied within groups sharing exactly the same allowed screens and remaining screen capacities, so no campaign is distributed to an unconfigured screen. Screens not explicitly routed receive only configured reserved and house content.

## Build and local run

Run `dotnet build src\Adrover.Functions\Adrover.Functions.csproj`. Set the required environment variables and run with Azure Functions Core Tools (`func start`) from this project directory. The SQL migration is `database\migrations\20261125-ad-scheduling.sql` and is additive/idempotent; apply it before enabling the Functions app.
