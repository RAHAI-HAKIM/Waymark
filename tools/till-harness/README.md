# till-harness

A hand-run check of the till's **window** (block A review, 24/09/2026). `TillScreenTests` argue
what the till shows without a window; nothing drove the window that draws it, and every defect the
review found in the till was the window's. This drives the real `TillWindow` headlessly, with
Avalonia's own headless platform and Skia, against a fake StoreServer that answers at once, and
saves a screenshot at each step.

```bash
dotnet run --project tools/till-harness -- <directory for the screenshots>
```

It prints one line per check and exits 1 on any failure. It needs no StoreServer, no database and
no display, so it runs on Windows and in a Linux container alike. It is outside `src/` and the
solution: CI does not build it, and its two packages (`Avalonia.Headless`, `Avalonia.Skia`, both
12.1.2 like the till) are pinned in its own project file rather than in
`src/Directory.Packages.props`. Whether it becomes a test project in CI is **O-30**.

## What it checks

| Check | The defect it caught (D-084) | Fails when this is taken out |
| :---- | :---- | :---- |
| A. the line just scanned is in view | The ticket stayed at its first line while the thirtieth scan went in out of sight | `CartFollow` |
| B. a redraw that is not a scan keeps the cashier's place | Every redraw — the 15 s clock, a health check — put a long ticket back at its first line | `TillScreen.Compare` and the kept scroll panel, together |
| C. a line taken out keeps the cashier's place | The same, on a redraw of the ticket itself | The kept scroll panel |
| D. a scan of a line out of view brings it into view | A second unit on a line further up counted out of sight | `CartFollow` |
| E. touching a line opens its actions in view | "Retirer la ligne" opened below the fold | `CartFollow` |
| F. touching a line leaves the search field focused | The window took the focus | — (the redraw gives it back) |
| G. a code typed after touching a line is sent by Entrée | Entrée went to the window, and the code sat in the field unsent | — (as F) |
| H. a touch on empty space leaves the code typed next to Entrée | The same, with no redraw to give the focus back | The window not focusable when signed in |
| I. a key takes a touch across its face, and leaves the focus in the search field | The staff chip and "Ignorer" took a touch only on their letters; a key touched kept the focus, and the next Entrée went to it | The key's transparent ground; `GettingFocus` cancelled for a touch; the window not focusable |
| J. Fluent's accent is the palette's action colour in both themes | It was Windows' accent colour: red on a till set to red | `FluentTheme.Palettes` in `App` |
| K. selected text in the search field is the palette's action pair | Selected text was drawn in that accent | The field's selection brushes |
| L. the list of who may sign in is asked for once the server answers | A till started before StoreServer showed nobody, and "HORS LIGNE", until restarted | The reload in `CheckHealthAsync` (`SignInFlow.NeedsList`) |

Each check failed against the code before its fix, and the third column was broken on purpose
(D-012) to see it fail again. F and G guard the symptom rather than one fix: a redraw after the
touch and the window's own focusability both keep them green.

## What it does not cover

- **A real Windows window.** Focus, input and rendering go through Avalonia's headless platform,
  not Win32: the Windows accent colour itself, the touch screen's gestures and DPI scaling are
  not exercised. Look at the till on the till for those.
- **StoreServer.** The fake answers every lookup with a product at 143,00 DA and every sale as
  completed. The real server's answers are `StoreServerStartupTests`' (Windows only).
- **One private member is reached by reflection**, `TillWindow.CheckHealthAsync`, so check J does
  not wait ten seconds for the health timer. If it is renamed the harness says so and stops.
