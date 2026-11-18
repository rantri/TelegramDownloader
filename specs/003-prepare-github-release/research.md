# Research decisions

| Decision | Rationale | Alternative considered |
| --- | --- | --- |
| Start public history at a clean root. | A live validation record and source examples in earlier local commits identify a private post. The remote has no refs. | Rewriting every private commit adds complexity without helping installation. |
| Keep Windows x64 and SDK 11.0.100. | The target uses WinForms and current-user DPAPI, and the existing self-contained publisher checks its output. | Linux runners cannot execute all Windows-specific tests. |
| Verify pushed revisions individually and PR merge revisions separately. | Testing only the tip hides intermediate failures in multi-commit pushes. | Running checks only on main would give fork authors late feedback. |
| Use a stable three-part project version with maintainer-created tags. | The application already identifies as 2.0.0. An explicit tag gives maintainers control of public releases. | Publishing on every push would assign release status to development snapshots. |
| Ship an archive, checksum and bundled licenses. | The existing single executable can move independently, while a ZIP provides a natural place for offline attribution. | An installer would add another application and update path to maintain. |
| Use verified artifacts as the input to publication. | A failing build or version check must stop publication, and fork jobs need read-only credentials. | Building again in a privileged job would widen its permissions and change the tested input. |

GitHub documents the push and pull request event payloads and the draft-to-public release operation. [Workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows) · [GitHub CLI release creation](https://cli.github.com/manual/gh_release_create)
