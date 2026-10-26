# Contributing

## Build and test

```
dotnet build
dotnet test
```

Every change must build with no warnings and pass all tests.

## Rules

- Do not use en dashes or em dashes anywhere. A test fails the build if one appears.
- Store all money as whole paise (`Money`). No floating point in money, tax, or quantity logic.
- Every calculation needs a test. Write the test first.
- Keep on-screen text short and clear. No filler.

## Commits

Use conventional prefixes: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `ci:`.
