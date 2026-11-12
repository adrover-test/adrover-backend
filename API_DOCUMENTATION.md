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
**POST** `/auth/login`

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

### 1. Create Payment Order
**POST** `/payments/orders`

**Request Body:**
```json
{
  "advertisementId": "uuid",
  "amount": 50000,
  "description": "Payment for Apollo campaign"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "orderId": "order_123456789",
    "amount": 50000,
    "currency": "INR",
    "status": "created",
    "createdAt": "2026-11-12T10:30:00Z"
  },
  "message": "Payment order created successfully"
}
```

---

### 2. Verify Payment
**POST** `/payments/verify`

**Request Body:**
```json
{
  "orderId": "order_123456789",
  "paymentId": "pay_123456789",
  "signature": "razorpay_signature_hash"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "paymentId": "pay_123456789",
    "status": "Completed",
    "amount": 50000,
    "verifiedAt": "2026-11-12T10:35:00Z"
  },
  "message": "Payment verified successfully"
}
```

**Error Codes:**
- `PAY_001` - Invalid signature
- `PAY_002` - Order not found
- `PAY_003` - Amount mismatch

---

### 3. Get Payment History
**GET** `/payments/history`

**Query Parameters:**
- `skip` (default: 0)
- `take` (default: 10)
- `status` (optional) - Completed, Pending, Failed

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "paymentId": "pay_123456789",
        "orderId": "order_123456789",
        "advertisementId": "uuid",
        "amount": 50000,
        "status": "Completed",
        "paymentMethod": "Card",
        "transactionDate": "2026-11-12T10:35:00Z"
      }
    ],
    "total": 15,
    "skip": 0,
    "take": 10
  }
}
```

---

### 4. Get Payment Details
**GET** `/payments/{paymentId}`

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "paymentId": "pay_123456789",
    "orderId": "order_123456789",
    "advertisementId": "uuid",
    "advertisementName": "Apollo Campaign",
    "amount": 50000,
    "status": "Completed",
    "paymentMethod": "Card",
    "lastFourDigits": "4242",
    "issuer": "Visa",
    "transactionDate": "2026-11-12T10:35:00Z",
    "receipt": "https://blob.azure.com/receipt.pdf"
  }
}
```

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

### 1. Upload Advertisement Video
**POST** `/files/upload-video`

**Request (multipart/form-data):**
- `file` (required, MP4/WebM, max 100MB)
- `advertisementId` (required)

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "url": "https://blob.azure.com/videos/xxx.mp4",
    "fileSize": 52428800,
    "duration": 30,
    "uploadedAt": "2026-11-12T10:35:00Z"
  },
  "message": "Video uploaded successfully"
}
```

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
