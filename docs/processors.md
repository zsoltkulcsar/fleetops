# Notification processors

Demo delivery makes no network calls and uses no real provider credentials.

| Port | Adapter | Data handled | Storage |
| --- | --- | --- | --- |
| IEmailSender | DemoEmailSender | Recipient address and masked, localized message | Process-local test inbox, deduplicated by notification ID |
| IPushSender | DemoPushSender | Subscription endpoint and generic localized body | Process-local recorded push |
| IEnrolmentSms | LimitedEnrolmentSms wrapping DemoEnrolmentSms | Phone and enrolment code; phone-change notice | Existing explicit Demo test inbox; no provider call |

Real e-mail, push and SMS providers require a user decision before Pilot. No real adapters are selected.
Push subscription endpoint and encryption keys are stored only for the signed-in office employee. VAPID configuration
is supplied externally as Push:VapidPublicKey and Push:VapidPrivateKey. Missing or invalid keys disable push.
Push bodies contain no names or document types. Enrolment SMS contains the authentication code, never a person's name
or document type; phone-change SMS is generic. Codes are not application logs.

SMS reserves one message atomically in sms_daily_usage for the company date before the Demo adapter is called.
The reservation persists on later failures, conservatively protecting the daily limit. sms.daily_limit defaults to 50;
zero or exhaustion refuses with sms_limit_reached. No provider credentials are committed.

## Jobs

notifications-digest queues a nonempty masked digest once per office user and company date, at or after 07:00.
alerts-escalation queues the owner and the other office role once, after alerts.escalation_minutes (default 15).
An acknowledged A-07 defect does not escalate. EscalatedAt and delivery records commit atomically.
Cover includes both original recipients and their active colleague for the inclusive company dates; no recursive cover routing.
Cover adds queues to Attention without granting medical access or additional command permissions.

Jobs are registered for the existing authenticated /jobs/{name} cron endpoint and the owner's manual admin trigger.
Hosts with background execution may opt in using Jobs:EnableNotificationScheduling=true (one-minute checks).
Digest eligibility uses company time, including DST; unique per-date rows prevent duplicate sends after restarts.
Demo defaults to manual triggering: Escalation runs on the manual trigger here; timing is not claimed.
