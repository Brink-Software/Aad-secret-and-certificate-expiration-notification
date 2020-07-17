# Introduction 
This repo contains an Azure Function (v3 / .Net Core 3.1) that will publish events using the Azure Event Grid when a secret or certificate belonging to an Azure Active Directory App Registration is about to expir.

Function execution is based on a timer. Each run the configurated Azure Active Directory will be queried for about to expire app registration secrets and certificates. After the first run it will only notify on new about to expire app registration secrets and certificates.

# Configuration
The function needs to know what Active Directory to query and to what Event Grid Topic events must be published.

| Application Setting            | Description                                     
| ------------------------------ | ----------------------------------------------- | 
| EventGridTopicUriSetting | The [url of an Event Grid Topic](https://docs.microsoft.com/en-us/azure/event-grid/post-to-custom-topic#endpoint)
| EventGridTopicKeySetting | The [key of an Event Grid Topic](https://docs.microsoft.com/en-us/azure/event-grid/get-access-keys)
| NotificationConfiguration__ExpirationThresholdInDays | The number of days left before a secret or certificate expires and an event will be send)
| GraphServiceCredentials__AppId |The app id of an app registration that has [permissions to read the Azure Active Directory Application Data](https://docs.microsoft.com/en-us/graph/permissions-reference#application-resource-permissions)
| GraphServiceCredentials__TenantId | The tenant id
| GraphServiceCredentials__ClientSecret | The app secret

If, for example, the NotificationConfiguration__ExpirationThresholdInDays is set to 14 an event will be send to notify that the secret or certificate will expire in two weeks.

# Published events
Two types of events can be published. Both share the same subject, consisting of the tenant id and the application id ({tenantId}/{appId}).

### Expiring certificates
```json
{
  "id": "116e2ff6-1da3-4c63-8927-5104851adc17",
  "subject": "cb85aff8-b452-4894-b122-0b9ba95d611a/16903c3a-5f05-4b39-bfb1-134975b62dbe",
  "data": {
    "AppRegistration": {
      "DisplayName": "My App Registration",
      "CreatedDateTime": "2018-10-18T13:12:01+00:00",
      "AppId": "16903c3a-5f05-4b39-bfb1-134975b62dbe"
    },
    "DaysLeft": 5,
    "DisplayName": "DC=MyCertificate",
    "Thumbprint": "15152E7CC2FCA3B18332DA3DEAF670A8A151CBC8",
    "StartDateTime": "2018-10-18T13:12:01+00:00",
    "EndDateTime": "2018-10-23T13:12:01+00:00",
  },
  "eventType": "Ibis.AzureActiveDirectory.ExpiringCertificate",
  "dataVersion": "1.0",
  "metadataVersion": "1",
  "eventTime": "2020-07-15T08:10:02.6233177Z",
  "topic": "/subscriptions/f469f487-ae2f-4c42-8265-c7a8f749dcb2/resourceGroups/rg-monitoring/providers/Microsoft.EventGrid/topics/mytopic"
}
```

### Expiring secrets
```json
{
  "id": "d76bd786-c0d8-4f1b-8f50-f4840cf700b4",
  "subject": "cb85aff8-b452-4894-b122-0b9ba95d611a/35869cd4-d5e6-4878-8f97-ce46bf52019e",
  "data": {
    "AppRegistration": {
      "DisplayName": "My App Registration",
      "CreatedDateTime": "2018-05-04T08:38:07+00:00",
      "AppId": "35869cd4-d5e6-4878-8f97-ce46bf52019e"
    },
    "DaysLeft": 10,
    "Description": "MySecret",
    "ValueHint": "fYu",
    "StartDateTime": "2018-10-18T13:12:01+00:00",
    "EndDateTime": "2018-10-28T13:12:01+00:00",
  },
  "eventType": "Ibis.AzureActiveDirectory.ExpiringSecret",
  "dataVersion": "1.0",
  "metadataVersion": "1",
  "eventTime": "2020-07-15T08:10:02.5001896Z",
  "topic": "/subscriptions/f469f487-ae2f-4c42-8265-c7a8f749dcb2/resourceGroups/rg-monitoring/providers/Microsoft.EventGrid/topics/mytopic"
}
```

