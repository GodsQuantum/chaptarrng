### Fixed

- **API:** Requests for provider-backed books that were deleted during refresh now return no match instead of raising repeated server errors.
---
category: fixed
audience: users
area: api
action: none
breaking: false
---
Readarr-compatible numeric book routes now tolerate provider or legacy identity records that point to a deleted local book row.
