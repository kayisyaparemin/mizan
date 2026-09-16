# Mizan Project Architectural Instructions

All code generation and modifications in this repository must strictly adhere to `docs/ARCHITECTURAL_RULES.md` and `ARCHITECTURE_RULES.md`.

Key rules:
- Strictly follow Clean Architecture boundaries.
- No God ViewModels (maximum 350 lines).
- ViewModels must not reference MAUI UI types (`Page`, `NavigationPage`, `Shell.Current`, etc.). Use `INavigationService` and `IDialogService`.
- No `async void` except in event handlers with mandatory try/catch.
- Never write tests that scrape source code files with `File.ReadAllText`. Write proper behavioral xUnit unit tests.
