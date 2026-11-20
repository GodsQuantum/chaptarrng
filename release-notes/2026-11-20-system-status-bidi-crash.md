---
category: fixed
audience: users
area: frontend
action: none
breaking: false
---
Opening System settings no longer crashes when status messages render. The shared bidi text component now uses the sanitizer export it imports, so status pages render while still stripping unsafe directional controls.
