# Installer language mapping

The easy installer only exposes localizations that are known to be useful for the LEGO Dimensions Xbox 360 build. It deliberately does **not** list every language ID supported by the Xbox 360 runtime: a platform language ID does not imply that this game contains assets for that language.

| Installer option | `user_language` | `user_country` | Type | Notes |
|---|---:|---:|---|---|
| English | 1 | 103 | Official | Original English localization |
| Deutsch | 3 | 24 | Official | German voice and text |
| Français | 4 | 34 | Official | French voice and text |
| Español (España) | 5 | 31 | Official | Spain variant |
| Español (Latinoamérica) | 5 | 71 | Official | Latin American variant; Mexico is used as the Xbox locale |
| Italiano | 6 | 50 | Official | Italian voice and text |
| Nederlands | 16 | 74 | Official | Dutch text/subtitles; English spoken dialogue |
| Dansk | 1 | 25 | Official | Denmark locale; Xbox 360 has no dedicated Danish `XGetLanguage` ID |
| Русский | 1 | 103 | Community | Existing bundled Russian text mod; English spoken dialogue |

## Why both language and country are written

ReXGlue exposes the Xbox 360 user configuration through `user_language` and `user_country`. Some LEGO Dimensions localizations share a language ID and use the country to choose a regional variant. Spanish is the clearest case: Spain and Latin America both use language ID 5, so the country value distinguishes them.

The installer writes both values to `legodimensions.toml` after the shared install job completes and records them in `install.json` alongside the other setup-owned TOML values.

## Russian

Russian is intentionally labelled as a community translation. Selecting it enables the existing `Lang_Russian_1` and `Lang_Russian_2` mods and keeps the underlying Xbox language on English. If a release payload does not contain the Russian translation and the mod tool, the option is not shown.

## Automatic default

The selector starts with the closest supported match to the Windows UI culture. Spanish Windows installations outside Spain default to the Latin American option. The choice is only a default: the user can select any available localization before installation.
