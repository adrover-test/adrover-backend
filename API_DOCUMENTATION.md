# Adrover Backend - API Documentation

## Base URL
```
https://localhost:7000/api/v1
```

## Authentication

All endpoints (except `/auth/*`) require JWT Bearer token in header:
```
Authorization: Bearer <access_token>
```

## Advertisement scheduling & playback

The daily Function App scheduler (10 operating hours per screen, allocating plays
from each campaign's inclusive start date, and filling freed commercial inventory
with the house ad), the Service Bus video-processing workflow (Intelisa Content ID),
the edit lock after first playback, and the incomplete-setup validation are
specified in [ADVERTISEMENT_SCHEDULING_API.md](ADVERTISEMENT_SCHEDULING_API.md).

## Response Format

### Success Response
```json
{
  "success": true,
  "data": { /* endpoint-specific data */ },
  "message": "Operation completed successfully",
  "timestamp": "2026-11-12T10:30:00Z"
}
```

### Error Response
```json
{
  "success": false,
  "error": {
    "code": "ERROR_CODE",
    "message": "Human-readable error message",
    "details": [ /* additional context */ ]
  },
  "timestamp": "2026-11-12T10:30:00Z"
}
```

## Status Codes
- `200 OK` - Successful GET, PUT, PATCH
- `201 Created` - Successful POST
- `204 No Content` - Successful DELETE
- `400 Bad Request` - Validation error
- `401 Unauthorized` - Missing or invalid token
- `403 Forbidden` - Insufficient permissions
- `404 Not Found` - Resource not found
- `422 Unprocessable Entity` - Business logic violation
- `429 Too Many Requests` - Rate limit exceeded
- `500 Internal Server Error` - Server error

---

## Driver directory and login

Drivers are stored in the separate `Drivers` table. Apply
`database/migrations/20261122-drivers.sql` where the table is absent.
The directory requires a SuperAdmin bearer token, obtained through
`POST /api/v1/auth/admin-login` with the administrator's email and password.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/v1/drivers` | List drivers without password hashes |
| POST | `/api/v1/drivers` | Create driver with a generated password |
| PUT | `/api/v1/drivers/{id}` | Edit driver, screen ID, and optionally replace the password atomically |
| POST | `/api/v1/drivers/{id}/password` | Replace a password |
| POST | `/api/v1/drivers/auth/login` | Sign in with `phone` and `password` |
| GET | `/api/v1/drivers/profile` | Current driver's basic profile |

Passwords are one-way BCrypt hashes and cannot be recovered. An administrator
must share a generated password before saving; resetting it invalidates existing
driver access tokens. Login is limited to five requests per minute per IP per
application instance. Deploy behind HTTPS and a shared rate limiter for
multi-instance deployments. Use a unique JWT signing key supplied through
configuration, not the development default. Public registration creates Client
accounts only; drivers are provisioned through the directory.

Apply `database/migrations/20261124-driver-operations.sql` after the driver
directory script. It is additive and rerunnable. It adds profile address,
language and private photo references, and separate `DriverAssignments`,
`DriverPlaybacks`, `DriverPayouts`, `DriverNotifications`, `DriverIssues` and
`DriverIssueHistory` tables. None of these require a driver entry in `Users`.

### Driver operations

Driver reads return the endpoint data directly, matching the driver frontend
(not the generic `data` envelope). Successful language and read-status changes,
ticket confirmation/reopening, and admin updates return `204 No Content`.

| Method | Route suffix under `/api/v1/drivers` | Purpose |
| --- | --- | --- |
| PUT | `/profile` | Save `name`, nullable `email`, nullable `address` |
| POST | `/profile/photo` | Multipart `photo`; JPEG/PNG, maximum 5 MB |
| PUT | `/language` | Save `preferredLanguage`: `en`, `hi`, or `te` |
| GET | `/vehicle` | Vehicle, screen ID and assignment availability |
| GET | `/dashboard` | Recorded playback, earnings and today's assignment totals |
| GET | `/advertisements/today` | Today's explicitly assigned schedules; `{items}` |
| GET | `/advertisements/history?from=yyyy-MM-dd&to=yyyy-MM-dd` | Owned playback grouped by campaign/day, maximum 366 days |
| GET | `/earnings?period=weekly\|monthly` | Lifetime earned/pending/processing/released totals, current-month earnings, selected-period per-ad earnings |
| GET | `/payments?period=weekly\|monthly` | Own payouts whose earned period overlaps the selected calendar period |
| GET | `/payments/{id}` | Own payout details |
| GET | `/notifications` | Own notifications and unread count |
| PUT | `/notifications/{id}/read` | Persist read status, `404` for unknown or other-driver IDs |
| GET | `/issues` | Own ticket summaries |
| GET | `/issues/{id}` | Ticket history and private attachment link |
| POST | `/issues` | Multipart ticket submission with UUID `Idempotency-Key` |
| POST | `/issues/{id}/confirm` | Confirm an unconfirmed resolved ticket |
| POST | `/issues/{id}/reopen` | Reopen an unconfirmed resolved/rejected ticket with `reason` (10–2000 characters) |

Ticket form fields: `category`, `description` (10–2000 characters),
optional `advertisementId` (the selected **assignment ID** returned as today's
schedule `id`), optional `vehicleId` (own driver's ID), `occurredAt`,
`contactMobile` (10-digit Indian mobile), `language`, and optional JPEG/PNG
`attachment` (5 MB). Retrying the same key returns the existing ticket; changed
ticket details with the same key return `409`. An attachment must not be changed
when retrying the same request.

All times and calendar periods are UTC; clients submit ISO timestamps with an
offset or `Z`. An assignment's play target covers the scheduled UTC day, from
its scheduled time to midnight. Today's ad IDs identify schedules, not global
campaigns. Historical playback is attributed through immutable driver
assignments, so changing a phone or vehicle does not transfer prior earnings.
Existing legacy `AdPlays` are preserved but are not automatically converted
into payable driver earnings without a verified assignment/rate.

### Administrator operations

All routes below require SuperAdmin and are under
`/api/v1/drivers/{driverId}`. They are available in **Cabs & drivers → View
details → Driver operations**.

| Method | Suffix | Request |
| --- | --- | --- |
| GET | `/operations` | Campaign choices, assignments, earnings, payouts and tickets |
| POST | `/assignments` | `advertisementId`, `scheduledAt`, `targetPlays` (1–1,000,000), `ratePerPlay` (INR; 0.0001–10,000, maximum 4 decimals) |
| PUT | `/assignments/{id}` | `status`: `assigned`, `paused`, `completed`; completed is final |
| POST | `/assignments/{id}/plays` | UUID `requestId`, positive `plays`, `playedAt`; independently verified playback only |
| POST | `/payouts` | Unique `transactionId`, `amount` (INR; 2 decimals), `periodStart`, `periodEnd`, `scheduledAt` |
| PUT | `/payouts/{id}` | `status`: `processing`, `released`, `failed`; external `reference` required for released |
| POST | `/notifications` | `title` (255 characters), `message` (2000 characters) |
| GET | `/issues/{id}` | Driver-scoped ticket details with private attachment/history |
| PUT | `/issues/{id}` | `status`: `under_review`, `in_progress`, `resolved`, `rejected`; required `note` |

Rates and schedules are immutable; pause/complete an assignment and create a
new one to change them. Assignments cannot overallocate the remaining campaign
target. Unused targets of completed assignments are released. Verified plays
cannot exceed an assignment or campaign target and must occur during the
assigned day, not in the future. Request IDs prevent duplicate accrual; amounts
are stored with four decimals and campaign `AdPlays`/`ActualPlays` are updated
in the same transaction. Drivers cannot self-report payable playback.

Payouts are **ledger records, not bank transfers**. Transfer funds externally
and then record the reference. Outstanding pending/processing/released
payouts reserve earned funds; failed payouts release the reservation.
Overlapping payout periods conservatively count all existing reservations to
prevent double payment of a period. Released/failed records are final; retry
failed transfers using a new transaction ID. Earnings pending means earned
funds less released/processing funds, including scheduled pending payouts.
Writes protecting assignment targets, payout balances and ticket transitions
use serializable transactions. Confirmed tickets are final; drivers must reopen
other closed tickets before an administrator can respond again.

Photos and attachments use the existing Azure storage service in the private
`driver-private` container and return read-only 15-minute SAS URLs. Configure
Azure Blob Storage credentials and keep this container non-public. Storage
failures are errors, not successful fake uploads. No device heartbeat integration
is supplied; an assigned screen's connection state is `unknown` rather than
invented online/offline telemetry. Playback status `playing` means assignment
progress, not a live player connection.

## Client dashboard

These routes are relative to the server origin (not `/api/v1`). They require an
authenticated Client account and return only that account's data.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/Advertisements/my-ads?pageNumber=1&pageSize=100` | Owned campaigns; `items`, `total`, `skip`, `take` |
| GET | `/api/ClientDashboard/summary` | Real campaign totals and 14-day recorded activity |
| GET | `/api/ClientDashboard/campaigns/{id}/creative` | Owner-checked private creative previews |
| GET | `/api/ClientDashboard/campaigns/{id}/playback?date=2026-11-18&pageNumber=1&pageSize=25` | Recorded plays for a UTC date |
| GET / PUT | `/api/Users/me` | Current profile; update supports the account `name` |
| GET | `/api/Payments/history?pageNumber=1&pageSize=10` | Account payment records |

Summary data contains `totalAdvertisements`, `activeAdvertisements`,
`pendingAdvertisements`, `totalPlays`, `activeCabs`, and `dailyPlays` (14
`{date, plays}` entries, including zero-play days, in UTC). `totalPlays` is the
sum of stored campaign delivery counters. `activeCabs` counts distinct vehicle
numbers with recorded play events during the last 14 days; it is not the sum of
requested campaign cab allocations.

Creative data is `{videoUrl, imageUrl}`. Missing assets are `null`; present assets
receive read-only URLs valid for 15 minutes. Another client's campaign returns
404. No storage credentials are sent to the browser. Refresh the preview to renew
expired URLs.

Playback data contains `items` and `total`. Each item exposes `id`,
`vehicleNumber`, `latitude`, `longitude`, `durationSeconds`, and `playedAt`.
Driver phone numbers are not returned. Page sizes must be 1-100; invalid page
ranges and future dates return 422. No recorded events means an empty list,
not generated vehicle locations.

Create Ad uses `POST /api/Advertisements`, followed by the existing authenticated
`/{id}/upload-video` and optional `/{id}/upload-image` routes. Invalid campaign
fields return 400 before any campaign is saved. Upload retries reuse the existing
campaign; creation and uploading do not collect payment. The client can finish
missing uploads from the saved campaign details. Profile edits persist supported
fields only; payment exports contain actual records and are not tax invoices.

The local integration host verifies creation/listing, owner isolation, exact
recorded totals, history pagination and private preview expiration using LocalDB
and fake storage/payment services. Restart a running API debug session after
adding the new controller; no production deployment or schema change is performed
by these tests.

## Authentication Endpoints

### 1. Register User
**POST** `/auth/register`

**Request Body:**
```json
{
  "email": "user@example.com",
  "phone": "919876543210",
  "password": "SecurePassword@123",
  "fullName": "John Doe",
  "role": "Client"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "userId": "uuid",
    "email": "user@example.com",
    "fullName": "John Doe",
    "role": "Client"
  },
  "message": "User registered successfully"
}
```

**Error Codes:**
- `AUTH_001` - Email already exists
- `AUTH_002` - Invalid email format
- `AUTH_003` - Password does not meet requirements
- `AUTH_004` - Invalid phone number

---

### 2. Login
**POST** `/auth/login` authenticates any supported account role.
**POST** `/auth/client-login` uses the same request and response, but only accepts active Client accounts. Client login uses the registered email as the username and does not require OTP.

Both endpoints require the existing `Users`, `RefreshTokens`, and `Wallets` tables; the user lookup also loads an optional wallet. `Users.PasswordHash` stores a password hash, not the password itself. Create client accounts using `/auth/register` or another trusted provisioning process.

**Request Body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePassword@123"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "eyJhbGciOiJIUzI1NiIs...",
    "expiresIn": 900,
    "tokenType": "Bearer"
  },
  "message": "Login successful"
}
```

**Error Codes:**
- `AUTH_005` - User not found
- `AUTH_006` - Invalid password
- `AUTH_007` - Account disabled

---

### 3. Refresh Token
**POST** `/auth/refresh`

**Request Body:**
```json
{
  "refreshToken": "eyJhbGciOiJIUzI1NiIs..."
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "expiresIn": 900
  },
  "message": "Token refreshed successfully"
}
```

---

### 4. Logout
**POST** `/auth/logout`

**Headers:** Requires JWT Bearer token

**Response (204 No Content)**

---

## User Endpoints

### 1. Get Current User
**GET** `/users/me`

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "userId": "uuid",
    "email": "user@example.com",
    "phone": "919876543210",
    "fullName": "John Doe",
    "role": "Client",
    "isActive": true,
    "profileImage": "https://blob.azure.com/...",
    "createdAt": "2026-11-12T10:00:00Z"
  }
}
```

---

### 2. Update User Profile
**PUT** `/users/{userId}`

**Request Body:**
```json
{
  "fullName": "John Updated",
  "phone": "919876543210",
  "profileImage": "base64-encoded-image"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": { /* updated user data */ },
  "message": "Profile updated successfully"
}
```

---

### 3. Change Password
**POST** `/users/{userId}/change-password`

**Request Body:**
```json
{
  "currentPassword": "OldPassword@123",
  "newPassword": "NewPassword@123"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Password changed successfully"
}
```

---

## Advertisement Endpoints

### Resumable registration

Apply `database\migrations\20261118-registration-drafts.sql` to existing databases
before running this backend. The migration is additive and can be run again safely.

- `POST /api/v1/auth/register` accepts an optional `advertisement` object for Client
  registration. The Client account and private draft are saved together. The object
  contains a client-generated UUID `id`, `name`, `brand`, `targetArea`, and
  `videoDurationSeconds`, with optional `days`, `dailyPlays`, `cabs`, `startDate`
  (`yyyy-MM-dd`), `step` (`plan`, `review`, or `payment`), and `consent`.
- After sign-in, `GET /api/Advertisements/drafts` lists the client's registrations;
  `GET /api/Advertisements/drafts/{id}` returns a single registration with saved
  progress, asset flags, and payment state. `PUT` on the same URL saves draft progress.
- Upload each file to `POST /api/Advertisements/{id}/upload-video` or `upload-image`.
  Retry only missing assets. Plan and asset changes lock when checkout starts.
- `POST /api/Payments/create-order` creates or reconciles the order for the campaign.
  Only a `Pending` order should open checkout. Submit Razorpay's order ID, payment ID
  and signature to `POST /api/Payments/verify`. A verified capture moves the draft to
  `PendingApproval`; a processing order must not open another checkout.

### 1. List Advertisements
**GET** `/advertisements`

**Query Parameters:**
- `status` (optional) - Filter by status: Active, Pending approval, Approved, Completed
- `skip` (default: 0) - Pagination offset
- `take` (default: 10) - Page size (max: 100)
- `sortBy` (optional) - Field to sort by: name, createdAt, status
- `sortOrder` (optional) - asc or desc (default: desc)

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "name": "Apollo Super Speciality Checkup",
        "brand": "Apollo Diagnostics",
        "status": "Active",
        "submittedAt": "2026-10-15T00:00:00Z",
        "startDate": "2026-10-15T00:00:00Z",
        "endDate": "2026-11-10T00:00:00Z",
        "targetPlays": 100000,
        "actualPlays": 68400,
        "assignedCabs": 100,
        "approvalAmount": 50000,
        "paidAmount": 50000,
        "paymentId": "uuid"
      }
    ],
    "total": 42,
    "skip": 0,
    "take": 10
  }
}
```

---

### 2. Get Advertisement Details
**GET** `/advertisements/{adId}`

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "name": "Apollo Super Speciality Checkup",
    "brand": "Apollo Diagnostics",
    "description": "Healthcare awareness campaign",
    "status": "Active",
    "videoUrl": "https://blob.azure.com/...",
    "imageUrl": "https://blob.azure.com/...",
    "submittedAt": "2026-10-15T00:00:00Z",
    "startDate": "2026-10-15T00:00:00Z",
    "endDate": "2026-11-10T00:00:00Z",
    "targetPlays": 100000,
    "actualPlays": 68400,
    "assignedCabs": 100,
    "targetArea": "Hitec City & Gachibowli",
    "approvalAmount": 50000,
    "paidAmount": 50000,
    "paymentStatus": "Paid",
    "approvedBy": "admin@adrover.com",
    "approvedAt": "2026-10-15T12:00:00Z"
  }
}
```

---

### 3. Create Advertisement (Submit for Approval)
**POST** `/advertisements`

**Request (multipart/form-data):**
- `name` (string, required) - Campaign name
- `brand` (string, required) - Brand name
- `description` (string, required) - Campaign description
- `targetPlays` (integer, required) - Target number of plays
- `assignedCabs` (integer, required) - Number of cabs for campaign
- `targetArea` (string, required) - Geographic targeting area
- `startDate` (datetime, required)
- `endDate` (datetime, required)
- `video` (file, optional) - Video file (MP4, max 100MB)
- `image` (file, optional) - Thumbnail image (JPG/PNG, max 10MB)

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "status": "Pending approval",
    "submittedAt": "2026-11-12T10:30:00Z"
  },
  "message": "Advertisement submitted for approval"
}
```

---

### 4. Update Advertisement (Before Approval)
**PUT** `/advertisements/{adId}`

**Request Body:** Same fields as create, excluding files

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Advertisement updated successfully"
}
```

---

### 5. Delete Advertisement (Pending Only)
**DELETE** `/advertisements/{adId}`

**Conditions:**
- Only pending approval ads can be deleted
- Only by owner or SuperAdmin

**Response (204 No Content)**

---

## Payment Endpoints

Payment routes below include the `/api` prefix. All except the signed webhook require `Authorization: Bearer <access-token>` and only allow access to the current user's payments.

**Flow:** create order -> frontend opens Razorpay Checkout -> customer authorizes payment -> backend verifies the signature and amount, captures if authorized, and returns the status -> frontend redirects only after `Completed`. Creating an order alone cannot capture payment or redirect the customer's browser.

### 1. Create Payment Order
**POST** `/api/payments/create-order`

**Request Body:**
```json
{
  "advertisementId": "uuid",
  "amount": 50000,
  "description": "Payment for Apollo campaign"
}
```

`amount` is in rupees and must match the advertisement's approved amount for a new order or the stored order amount when retrying. The backend reuses the saved order instead of creating duplicates. If the latest attempt failed and no authorization or capture exists, create-order returns `Pending` so checkout can reopen on the same order; the status endpoint still reports the failed attempt until it is superseded.

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "id": "internal-payment-uuid",
    "orderId": "order_123456789",
    "keyId": "rzp_test_public_key",
    "amount": 50000,
    "amountInPaise": 5000000,
    "currency": "INR",
    "status": "Pending",
    "createdAt": "2026-11-12T10:30:00Z"
  },
  "message": "Order created successfully"
}
```

Pass `amountInPaise`, not `amount`, to Razorpay Checkout. Only the public key ID is returned; the key secret must remain on the server.

### 2. Verify and Capture Payment
**POST** `/api/payments/verify`

Send Razorpay Checkout's success-handler values:
```json
{
  "orderId": "order_123456789",
  "paymentId": "pay_123456789",
  "signature": "razorpay_signature_hash"
}
```

The backend verifies the signature using its stored order ID, fetches the payment from Razorpay, checks its ID, order, amount and currency, then captures it if its status is `authorized`. Already-captured payments are not captured again. Repeating verification, including after a webhook has already completed the payment, is supported. Invalid signatures do not mark a valid order as failed.

**Response (200 OK, abbreviated):**
```json
{
  "success": true,
  "data": {
    "id": "internal-payment-uuid",
    "orderId": "order_123456789",
    "paymentId": "pay_123456789",
    "status": "Completed",
    "amount": 50000,
    "currency": "INR",
    "processedAt": "2026-11-12T10:35:00Z"
  },
  "message": "Payment completed successfully"
}
```

`success: true` means the API operation succeeded, not necessarily that payment completed. Always inspect `data.status`: `Pending` (no attempt yet), `Processing` (not yet captured), `Completed` (captured), `Failed`, or `Refunded`. Only `Completed` permits the payment-success redirect. Validation errors return 400, unauthenticated requests 401, another user's order 403, and unknown orders 404. Gateway failures return an error, never a fabricated successful payment.

### 3. Get Payment History
**GET** `/api/payments/history?pageNumber=1&pageSize=10`

`pageNumber` starts at 1; `pageSize` must be between 1 and 100. Returns stored payment DTOs in `data.items`, with `total`, `skip`, and `take`. History does not refresh Razorpay status.

### 4. Get Stored Payment Details
**GET** `/api/payments/{id}`

`id` is the internal payment UUID returned by create-order, not a Razorpay `pay_...` ID. Returns a payment DTO with `orderId`, `paymentId`, `amount`, `currency`, `status`, `paymentMethod`, `cardLastFour`, `cardIssuer`, `createdAt`, `processedAt` and `advertisementId`.

### 5. Refresh Payment Status
**GET** `/api/payments/orders/{orderId}/status`

Returns the same payment DTO after querying Razorpay. It can recover a payment even if the checkout success callback was lost, selects a successful/authorized attempt ahead of failed attempts, and captures an authorized payment if needed. This is a reconciliation endpoint with possible capture side effects; responses are not cacheable. Poll while status is `Pending` or `Processing`. Do not treat closing checkout as proof of failure; reconcile the order before offering a retry.

### 6. Frontend Checkout Example

Load `https://checkout.razorpay.com/v1/checkout.js` in the frontend. The frontend owns the payment page and final redirect. The following example assumes `accessToken`, `advertisementId`, and `approvedAmount` are available in the authenticated frontend, and that `/payment/success` is a frontend route:

```javascript
async function paymentApi(path, body) {
  const response = await fetch(`/api/payments/${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json"
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const result = await response.json();
  if (!response.ok || !result.success) {
    throw new Error(result.message || "Payment request failed");
  }
  return result.data;
}

async function reconcileOrder(orderId) {
  for (let attempt = 0; attempt < 20; attempt++) {
    const payment = await paymentApi(`orders/${encodeURIComponent(orderId)}/status`);
    if (payment.status === "Completed") {
      window.location.assign(`/payment/success?orderId=${encodeURIComponent(orderId)}`);
      return;
    }
    if (payment.status === "Failed" || payment.status === "Refunded") {
      alert(`Payment status: ${payment.status}`);
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 2000));
  }
  alert("Payment is still pending. Check its status again before retrying.");
}

async function startPayment() {
  const order = await paymentApi("create-order", {
    advertisementId,
    amount: approvedAmount,
    description: "Advertisement payment"
  });
  const checkout = new Razorpay({
    key: order.keyId,
    order_id: order.orderId,
    amount: order.amountInPaise,
    currency: order.currency,
    name: "Adrover",
    handler: async result => {
      try {
        await paymentApi("verify", {
          orderId: result.razorpay_order_id,
          paymentId: result.razorpay_payment_id,
          signature: result.razorpay_signature
        });
        await reconcileOrder(order.orderId);
      } catch (error) {
        alert(`${error.message}. Check this order's status before retrying.`);
      }
    },
    modal: {
      ondismiss: () => reconcileOrder(order.orderId).catch(error => alert(error.message))
    }
  });
  checkout.on("payment.failed", event => {
    alert(event.error.description || "Payment attempt failed. You can retry in checkout.");
  });
  checkout.open();
}

// Call from a Pay button; show create-order and checkout errors to the customer.
// startPayment().catch(error => alert(error.message));
```

Persist the returned `orderId` in the frontend's order state so the status check can resume after navigation/reload. A success URL is not proof of payment: the destination page must retrieve the server status too. For a separately hosted frontend, use the backend base URL and configure its existing `AllowedOrigins` setting.

### 7. Razorpay Webhook
**POST** `/api/payments/webhook`

Configure this HTTPS URL in the Razorpay dashboard and set the matching `Razorpay:WebhookSecret` on the server. Subscribe to `payment.authorized`, `payment.captured`, `payment.failed`, and `order.paid`. The backend validates `X-Razorpay-Signature`, checks the order amount/currency, then reconciles against Razorpay, including capturing authorized payments when the browser has closed. Duplicate or delayed events do not downgrade a completed payment. Ignored event types return 200. Configure `Razorpay:KeyId` and `Razorpay:KeySecret` through secure configuration; use test-mode credentials to validate checkout before live payments.

---

## Wallet Endpoints

### 1. Get Wallet Balance
**GET** `/wallets/balance`

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "walletId": "uuid",
    "userId": "uuid",
    "balance": 15000,
    "currency": "INR",
    "lastUpdated": "2026-11-12T10:30:00Z"
  }
}
```

---

### 2. Add Wallet Balance
**POST** `/wallets/topup`

**Request Body:**
```json
{
  "amount": 10000,
  "paymentId": "pay_123456789"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "transactionId": "uuid",
    "previousBalance": 5000,
    "newBalance": 15000,
    "amount": 10000,
    "timestamp": "2026-11-12T10:35:00Z"
  },
  "message": "Wallet topped up successfully"
}
```

---

### 3. Get Wallet Transactions
**GET** `/wallets/transactions`

**Query Parameters:**
- `skip` (default: 0)
- `take` (default: 10)

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "transactionId": "uuid",
        "type": "Credit",
        "amount": 10000,
        "balanceBefore": 5000,
        "balanceAfter": 15000,
        "description": "Top up via payment",
        "timestamp": "2026-11-12T10:35:00Z"
      }
    ],
    "total": 42,
    "skip": 0,
    "take": 10
  }
}
```

---

## Notification Endpoints

### 1. Get Notifications
**GET** `/notifications`

**Query Parameters:**
- `skip` (default: 0)
- `take` (default: 10)
- `isRead` (optional) - true or false

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "notificationId": "uuid",
        "title": "Campaign Approved",
        "message": "Your Apollo campaign has been approved",
        "type": "CampaignApproved",
        "isRead": false,
        "relatedId": "ad-uuid",
        "createdAt": "2026-11-12T10:30:00Z"
      }
    ],
    "total": 12,
    "unread": 3,
    "skip": 0,
    "take": 10
  }
}
```

---

### 2. Mark Notification as Read
**PUT** `/notifications/{notificationId}/read`

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Notification marked as read"
}
```

---

### 3. Mark All Notifications as Read
**PUT** `/notifications/read-all`

**Response (200 OK):**
```json
{
  "success": true,
  "message": "All notifications marked as read"
}
```

---

## File Upload Endpoints

### 1. Attach a Video to an Advertisement

**POST** `/api/Advertisements/{id}/upload-video`

**Authorization:** Bearer token for the client that owns the advertisement.

**Request:** `multipart/form-data` with a `file` field containing a non-empty
MP4, WebM, or MOV file up to 100 MiB. Create the advertisement first using
`POST /api/Advertisements` and use its returned ID. The backend uploads the file
to the `advertisements` blob container and updates the advertisement's
`VideoUrl`. Campaign creation and video upload are separate requests; if the
upload fails, retry it with the same advertisement ID rather than creating a
second campaign.

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "url": "https://example.blob.core.windows.net/advertisements/ads/example/video.mp4",
    "fileSize": 52428800,
    "uploadedAt": "2026-11-12T10:35:00Z"
  },
  "message": "Video uploaded successfully"
}
```

The returned URL is not a SAS URL. Private blobs require authorized access or a
short-lived SAS URL. Azure App Service must have `AzureBlob__ConnectionString`
configured; do not put storage credentials in the frontend.

---

### 2. Upload Advertisement Image
**POST** `/files/upload-image`

**Request (multipart/form-data):**
- `file` (required, JPG/PNG, max 10MB)
- `advertisementId` (required)

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "url": "https://blob.azure.com/images/xxx.jpg",
    "fileSize": 2097152,
    "uploadedAt": "2026-11-12T10:35:00Z"
  },
  "message": "Image uploaded successfully"
}
```

---

### 3. Upload Profile Image
**POST** `/files/upload-profile-image`

**Request (multipart/form-data):**
- `file` (required, JPG/PNG, max 5MB)

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "url": "https://blob.azure.com/profiles/xxx.jpg",
    "uploadedAt": "2026-11-12T10:35:00Z"
  }
}
```

---

## Admin Endpoints

### 1. List Pending Approvals
**GET** `/admin/approvals/pending`

**Authorization:** SuperAdmin only

**Query Parameters:**
- `skip` (default: 0)
- `take` (default: 10)

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "advertisementId": "uuid",
        "name": "CyberFintech App Launch",
        "brand": "Sample Fintech",
        "submittedBy": "client@example.com",
        "submittedAt": "2026-11-02T10:00:00Z",
        "status": "Pending approval",
        "estimatedPlays": 50000,
        "estimatedCost": 75000
      }
    ],
    "total": 5,
    "skip": 0,
    "take": 10
  }
}
```

---

### 2. Approve Advertisement
**POST** `/admin/advertisements/{adId}/approve`

**Authorization:** SuperAdmin only

**Request Body:**
```json
{
  "approvalAmount": 75000,
  "estimatedPlays": 50000,
  "notes": "Approved for campaign launch"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Advertisement approved successfully"
}
```

---

### 3. Reject Advertisement
**POST** `/admin/advertisements/{adId}/reject`

**Authorization:** SuperAdmin only

**Request Body:**
```json
{
  "reason": "Brand name conflicts with policy guidelines"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Advertisement rejected"
}
```

---

### 4. Get System Analytics
**GET** `/admin/analytics`

**Authorization:** SuperAdmin only

**Query Parameters:**
- `dateFrom` (optional) - ISO date format
- `dateTo` (optional) - ISO date format

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "totalUsers": 245,
    "totalAdvertisements": 42,
    "activeAdvertisements": 15,
    "totalRevenue": 2500000,
    "totalPlays": 5000000,
    "averagePlayDuration": 28,
    "topBrand": "Apollo Diagnostics",
    "topArea": "Hitec City & Gachibowli"
  }
}
```

---

## Health Check Endpoints

### 1. API Health Status
**GET** `/health`

**Response (200 OK):**
```json
{
  "status": "Healthy",
  "timestamp": "2026-11-12T10:30:00Z",
  "uptime": "2d 14h 30m",
  "version": "1.0.0"
}
```

---

### 2. Database Health Check
**GET** `/health/database`

**Response (200 OK):**
```json
{
  "status": "Connected",
  "responseTime": 45,
  "lastChecked": "2026-11-12T10:30:00Z"
}
```

---

## Rate Limiting

Rate limits apply per user/IP:
- **Authentication endpoints**: 5 requests per minute
- **Payment endpoints**: 10 requests per minute
- **File upload**: 100 MB per hour per user
- **General endpoints**: 100 requests per minute

Headers in response:
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1605120600
```

---

## Pagination

Standard pagination parameters for list endpoints:
- `skip` - Number of records to skip (default: 0)
- `take` - Number of records to take (default: 10, max: 100)
- `sortBy` - Field name to sort by
- `sortOrder` - "asc" or "desc" (default: desc)

Response includes:
- `items` - Array of results
- `total` - Total number of items available
- `skip` - Current skip value
- `take` - Current take value

---

## Error Codes Reference

| Code | Status | Meaning |
|------|--------|---------|
| AUTH_001 | 400 | Email already exists |
| AUTH_002 | 400 | Invalid email format |
| AUTH_003 | 400 | Password does not meet requirements |
| AUTH_004 | 400 | Invalid phone number |
| AUTH_005 | 404 | User not found |
| AUTH_006 | 401 | Invalid password |
| AUTH_007 | 403 | Account disabled |
| AUTH_008 | 401 | Token expired |
| AUTH_009 | 401 | Invalid token |
| AUTH_010 | 403 | Insufficient permissions |
| PAY_001 | 400 | Invalid signature |
| PAY_002 | 404 | Order not found |
| PAY_003 | 400 | Amount mismatch |
| PAY_004 | 422 | Payment already processed |
| AD_001 | 404 | Advertisement not found |
| AD_002 | 422 | Cannot modify approved advertisement |
| AD_003 | 400 | Invalid advertisement data |
| AD_004 | 422 | Active campaign already exists |
| VAL_001 | 400 | Validation failed |
| SRV_001 | 500 | Internal server error |

---

## Webhooks (Future Implementation)

The following webhooks will be implemented for real-time updates:

- `payment.completed` - Payment successful
- `payment.failed` - Payment failed
- `campaign.approved` - Campaign approved by admin
- `campaign.rejected` - Campaign rejected
- `campaign.started` - Campaign started displaying
- `campaign.ended` - Campaign ended

Webhook setup and credentials needed in configuration.

---

## Testing

### Test Account Credentials (Development)
```
Email: demo@adrover.com
Password: Demo@123456
Phone: 919876543210
```

### Razorpay Test Cards
- Visa: 4242 4242 4242 4242
- Mastercard: 5105 1051 0510 5100
- CVV: Any 3 digits
- Expiry: Any future date

---

## CORS Policy

Allowed origins (configurable in appsettings.json):
- `http://localhost:5173` (Vite dev)
- `http://localhost:5174` (Mobile dev)
- `https://yourdomain.com` (Production)

Allowed methods: GET, POST, PUT, PATCH, DELETE, OPTIONS

---

## Support

For API issues or questions:
1. Check error code in response
2. Review [ARCHITECTURE.md](ARCHITECTURE.md) for design details
3. Check [SETUP_GUIDE.md](SETUP_GUIDE.md) for configuration
4. Review logs in `logs/` directory
