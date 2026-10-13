# Development

Use .NET SDK 10 and your own SPT 4.1.6 / EFT 40743 client files:

```powershell
.\tools\Build.ps1 -SptPath 'D:\YourSPT'
```

The script runs the offline tests, compiles the plugin and copies it with the README and MIT license into `dist/BepInEx/plugins/Tylevo.ExamineAll/`. It does not install anything. Game, Unity and BepInEx assemblies are local references and aren't included in this repository or the release.

To run just the offline tests, without a game installation:

```powershell
dotnet run --project tests/ExamineAll.Tests.csproj -c Release
```

The build checks the SHA-256 of the supported game assembly. The plugin also checks its module ID at startup and disables itself on a different client build. Other SPT/EFT builds have not been validated.

## How examination works

The button reads the loaded item templates and skips items marked examined by default or already present in the active PMC's encyclopedia. It sends the remaining IDs through the native examination operation in batches of 256, without awarding XP.

A batch counts only after its operation queue flush succeeds without a warning or error. The immediate acceptance callback isn't treated as completion. The notification and log report newly confirmed examinations; a run with nothing to do reports zero.

Leaving the main menu stops new batches. An already submitted batch may still finish. Completion notifications wait until the same profile is back on the main menu and the notification manager is ready. Failed or timed-out requests require a game restart before another run.

SPT handles profile autosave. Scav knowledge follows the game's normal synchronization when it generates a scav or completes a raid.

## Validation

Version 0.1.2 compiled with no warnings or errors and passed 67 offline checks. These cover batching, duplicate IDs, cancellation, acknowledgements, timeouts and result notifications. They do not test the live Unity UI or prove that changes survived a game restart.

Still to check in game:

- Button placement and interaction at different resolutions.
- Unknown base-game and mod items become examined without XP; known items stay known.
- One completion alert reports the new-item count, and pressing again reports zero.
- Leaving the menu stops new batches and reports only confirmed progress on return.
- Installing a content pack and restarting makes its unknown items available to the next run.
- Changes remain after normal autosave and a client/server restart.

## Packaging

Package only the `BepInEx` folder from `dist/`. Keep the README and LICENSE with the plugin. Use forward slashes for ZIP entry paths. Old archives, build logs, generated files and local verification records are ignored by Git.
