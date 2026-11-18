# Publication data model

| Entity | Identity | Rules |
| --- | --- | --- |
| Revision | Full Git commit SHA | Associate each checked revision with its result. A PR uses its proposed merge revision. |
| Application version | Three decimal components, e.g. `2.0.0` | The project supplies the version printed by the binary. |
| Release | Stable `vMAJOR.MINOR.PATCH` tag and its revision | The stable tag must exactly match the application version. Publication follows successful verification. A public version is immutable in the workflow. |
| Package | `TelegramDownloader-<version>-win-x64.zip` | Contains the tested Windows x64 executable, MIT license and redistribution notices. The adjacent checksum identifies the complete archive. |

A release proceeds from a tagged revision to verified files to a public release. Failed checks terminate the run. An incomplete upload may be recovered while the release is still a draft.
