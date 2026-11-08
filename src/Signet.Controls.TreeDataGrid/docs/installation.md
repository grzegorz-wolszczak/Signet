# `TreeDataGrid` Installation

- Reference the `Signet.Controls.TreeDataGrid` project (not the upstream NuGet package).
- Add the theme to `App.axaml`, **after** `FluentTheme`:

```xml
<Application.Styles>
  <FluentTheme/>
  <StyleInclude Source="avares://Signet.Controls.TreeDataGrid/Themes/Fluent.axaml"/>
</Application.Styles>
```
