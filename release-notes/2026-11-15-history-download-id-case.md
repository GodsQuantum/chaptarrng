---
category: fixed
audience: operators
area: api
action: none
breaking: false
---
The history API's `downloadId` filter now finds a download regardless of letter case. Download clients such as SABnzbd report ids in lower case in the queue, while history stores them upper case, so `GET /api/v1/history?downloadId=` returned nothing for those downloads.
