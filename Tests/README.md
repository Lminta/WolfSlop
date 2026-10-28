# Engine regression checks

Run the commands in the [repository README](../README.md) from the engine
repository root. `EngineTests` covers map compilation and edits, walking,
input, timing, object/component ownership, doors, car physics/contact/cameras,
decorative animation, and portable music queue/fade/pause/disposal behavior.
It also generates sprite-sort reference data from seeded, procedural scenes.

`EngineConsumer` references the portable engine alone and checks its public
API, custom metadata/input/audio, rendering contracts, scene lifetime, retained
UI, and absence of browser dependencies. Node checks cover sprite selection and
ordering, texture uploads and cancellation, audio mixing, and UI drawing/input.

Game-specific maps, scene assembly, character presentation, racing, opponents,
sound configuration, and full-browser application checks live in the consuming
game repository. It can link `Shared/ReferenceSpriteSelection.cs` to compare
its own authored scenes with the same independent native ordering oracle.
