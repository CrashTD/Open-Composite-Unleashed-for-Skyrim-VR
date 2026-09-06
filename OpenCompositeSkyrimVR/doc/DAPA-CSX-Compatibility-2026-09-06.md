# DAPA CSX accepted-draw compatibility — 6 September 2026

## Cause and scope

The original adapter accepted only the Paintball test DLL's owner at RVA
0x105BD0. Regular CSX DLLs expose the same VR menu direct-draw hook at other
addresses. If that owner is chained at Skyrim's 0xDBDDF3 mesh call, rejecting it
aborts body-mask installation, leaving world correction unchanged.

Version strings cannot identify the contract: the regular and Paintball DLLs
both report 3.19.0.0. Startup now selects one of three independently verified
contracts, matching the owner address AND SHA-256 of its entire function.

| Actual binary | DLL SHA-256 | Owner RVA / size | Accepted draw RVAs |
| --- | --- | --- | --- |
| CSX 3.18.0.0 | BCDECB9906E1CB443726BF3ACB2EA8D9DA577DA9105CC06CE1A45C10CD7BA971 | E01A0 / 356 hex | E020C, E04B2 |
| Regular CSX 3.19.0.0 | 04CBC257643630798BAC9534C176A1A1B9074286AAC03B5D380809902975B9EA | F5460 / 384 hex | F54CC, F57A0 |
| Paintball CSX 3.19.0.0 | C192935E9A509835C6B71A562D48614D1DD23FA077E8F2622BBE051BDC95AFA5 | 105BD0 / 384 hex | 105C3C, 105F10 |

These are exact binaries, not a promise to support every build with those
version strings. Each has a fast-path native DrawIndexedInstanced and a second
native draw after the suppression predicate. Disassembly confirms the original
six-argument ABI at both calls, the suppressed path skipping the second draw,
and the bridge's engine caller marker 0xDBDDF9. The callbacks observe only those
accepted draws; no suppression predicate or original owner entry is replaced.

Full-function validation and contract selection occur only at startup. The
per-frame callback, ownership classification, mask replay and warp are unchanged.
The startup log names the selected contract even with verbose logging off.

## Verification

- Release SKSE plugin and DapaCsxDrawTest built successfully. The existing
  Main.cpp NiTArray size-conversion warning remains unrelated to this change.
- Actual DLL mappings (no entry-point/import execution): all three contracts
  selected correctly, both sites installed, complete function bytes restored.
- Unknown owner offsets, wrong contracts, and a mutation at every byte of each
  owner function were rejected.
- All six instruction fixtures passed six-argument ABI, owned/unowned draw and
  rollback tests. Existing engine tests passed all 17 sites and owner chaining.
- WARP and hardware GPU tests passed both eyes, suppression/redirect isolation,
  scene colour/state preservation, normal/reversed D24S8/D32 and biased depth.
- HIGGS and SpellWheel ownership regression tests passed.

## Remaining live verification

Deployed on 6 September to both local OCU MO2 folders and the Nexus staging
folder/ZIP, with shipping capture disabled and settings preserved. Not yet
tested in Skyrim with these older binaries. For each build,
check the named adapter startup line and increasing player mask coverage, then
test strafing/stick-turning with body, equipped geometry, HIGGS objects and
SpellWheel. Check CSX menu capture/suppression remains correct. Unknown owners
continue to fail closed; do not remove validation to accommodate another build.

Open Shaders 2.11.0 was separately inspected from Alandtse's published
CommunityShaders-2026-09-04T04-49Z.7z. Its DLL hash is
C81DDF331E03FDEF5753D9703D04AE50B467720C97D2BC6476F63EDDC970B458.
Its Upscaling.cpp at that tag does not contain this CSX direct-draw hook, and the
binary does not contain the same two-call candidate pattern. That is NOT an
end-to-end compatibility result: no speculative Open Shaders adapter was added.
An unchained engine draw uses the existing engine path; any other owner still
requires separate verification.

No CSX, HIGGS or SpellWheel binaries or MO2 settings were changed. Deployment
includes the logging cleanup, and the Nexus ZIP was verified against every
staged file. Git publication was separately authorized by the user; this does
not constitute an uploaded Nexus release or a live headset compatibility test.
