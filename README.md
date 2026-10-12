# Tylevo's Examine All

For those who don't have SVM installed for whatever reason.

Adds an **Examine all** button to the bottom-left corner of the main menu. Click it, let it finish, and a notification tells you how many new items were examined.

It skips items you already know and includes items added by mods. Added another content pack? Restart SPT and press it again. It runs only when you click it and doesn't give examination XP.

## Install

Requires **SPT 4.1.6 / EFT 40743** and BepInEx 5, included with SPT. No server mod or SVM needed.

1. Download `Tylevo.ExamineAll-0.1.1-Test.zip` from [Releases](https://github.com/Tylevo/Tylevo.ExamineAll/releases).
2. Close the game and extract the ZIP into your SPT folder, where `EscapeFromTarkov.exe` is.
3. Start the game and click **Examine all** on the main menu.

The plugin should end up here:

```text
BepInEx/plugins/Tylevo.ExamineAll/Tylevo.ExamineAll.dll
```

Stay on the main menu while it runs. Leaving the menu stops further batches. If it asks you to restart, restart the game before trying again.

It updates your active PMC profile and uses SPT's normal autosave. Let the server save before closing it. To uninstall, remove the `Tylevo.ExamineAll` plugin folder; items you've examined stay examined.

## Build status

**0.1.1 is a test release.** Compilation and 67 offline checks passed. The menu, notification and saved results still need an in-game check.

Build instructions and the test checklist are in [Development](https://github.com/Tylevo/Tylevo.ExamineAll/blob/main/docs/DEVELOPMENT.md). If something goes wrong, open an [issue](https://github.com/Tylevo/Tylevo.ExamineAll/issues) with your SPT version and the relevant lines from `BepInEx/LogOutput.log`.
