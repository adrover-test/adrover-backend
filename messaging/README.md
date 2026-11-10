# Adrover Messaging API

Minimal ASP.NET Core (.NET 10) API with one endpoint that can send:

- Outlook email to `admin@adrover.in` (Microsoft Graph)
- WhatsApp messages (Meta WhatsApp Cloud API)
- SMS messages (MSG91 Flow API)

No extra NuGet packages are used.

## Configure

Store secrets outside source control, from `src\Adrover.Messaging.Api`:

```powershell
dotnet user-secrets set "Messaging:ApiKey" "<long-random-key-for-callers>"

dotnet user-secrets set "Outlook:TenantId" "<entra-tenant-id>"
dotnet user-secrets set "Outlook:ClientId" "<app-client-id>"
dotnet user-secrets set "Outlook:ClientSecret" "<app-client-secret>"
dotnet user-secrets set "Outlook:SenderEmail" "<mailbox-that-sends>@adrover.in"

dotnet user-secrets set "WhatsApp:AccessToken" "<meta-access-token>"
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<whatsapp-phone-number-id>"

dotnet user-secrets set "Msg91:AuthKey" "<msg91-auth-key>"
dotnet user-secrets set "Msg91:SmsTemplateId" "<dlt-approved-flow-template-id>"
```

In production use environment variables such as `Outlook__ClientSecret`.

- Outlook: the Entra app needs the Microsoft Graph **Mail.Send** application
  permission with admin consent. Email always goes to `Outlook:AdminEmail`
  (`admin@adrover.in`); callers cannot change the recipient.
- WhatsApp: free-form text is delivered only within 24 hours of the user's last
  message. Otherwise send an approved template using `templateName`.
- SMS: India requires a DLT-approved MSG91 template. `message` fills the
  template variable named by `Msg91:MessageVariable`, or pass `variables`.

## Run

```powershell
dotnet run --project src\Adrover.Messaging.Api --launch-profile http
```

## Endpoint

`POST /api/messages` with header `X-Api-Key: <Messaging:ApiKey>`

```json
{
  "channels": ["email", "whatsapp", "sms"],
  "to": "9876543210",
  "subject": "New Adrover enquiry",
  "message": "Name: Ravi\nBusiness: Ravi Foods\nObjective: Brand awareness",
  "replyTo": "ravi@example.com"
}
```

- `channels`: any of `email`, `whatsapp`, `sms`.
- `to`: mobile number for WhatsApp/SMS. 10-digit Indian numbers get `91` added.
- WhatsApp template: add `templateName`, optional `languageCode` and `parameters`.
- SMS template variables: add `"variables": { "name": "Ravi" }`.

Response contains one result per channel. HTTP 200 means at least one channel
sent; HTTP 502 means all failed; HTTP 400 means invalid input (nothing sent).

```json
{
  "sent": true,
  "results": [
    { "channel": "email", "sent": true, "to": "admin@adrover.in" },
    { "channel": "whatsapp", "sent": true, "to": "919876543210", "providerMessageId": "wamid..." },
    { "channel": "sms", "sent": true, "to": "919876543210" }
  ]
}
```

Keep `Messaging:ApiKey` on a trusted server; do not place it in browser code.
