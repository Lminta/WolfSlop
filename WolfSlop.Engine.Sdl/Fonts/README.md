# Bundled fonts

The native UI uses Noto Sans and Roboto Mono from the Google Fonts repository:

- https://github.com/google/fonts/tree/main/ofl/notosans
- https://github.com/google/fonts/tree/main/ofl/robotomono

Both are distributed under the SIL Open Font License. The accompanying license
files are copied into application output. Bundling these fonts keeps text drawing
and measurement available without system font discovery on Linux and Android.
