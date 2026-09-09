# Runtime UI state checks

PASS tutorial uses first 3 unlocked formation slots

PASS roster edits do not commit last battle team

PASS back discards roster draft

PASS view presets then return preserves roster draft

PASS saving a preset affects only that preset

PASS explicitly enabled preset loads into roster

PASS non-full formation requires confirmation

PASS empty formation cannot enter battle

PASS battle entry commits team and grants actual starter count

PASS battle team is a snapshot, not draft alias

PASS current clear and element badges do not borrow historical perfect badge

PASS failure earns no badges and preserves historical record

PASS paused menu does not allow economy mutations

PASS return to levels clears battle state and speed

PASS saving empty active preset clears enabled state

PASS empty preset cannot be enabled

Scope: real menu state handlers in Windows player; not a physical pointer/end-to-end input test. Capture mode does not write the user's profile.
