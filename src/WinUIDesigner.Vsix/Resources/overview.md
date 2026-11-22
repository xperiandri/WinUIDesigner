# Overview

![preview](https://github.com/0x5bfa/WinUIDesigner/blob/main/src/WinUIDesigner.Vsix/Resources/preview.png?raw=true)

Having trouble? Please [report a bug on GitHub](https://github.com/0x5bfa/WinUIDesigner/issues/new?template=bug_report.yml).

## Limitations

- Only C# WinUI projects are supported.
- x86 is not supported.
- `Window` and its subclasses cannot be previewed; only `FrameworkElement` subclasses are supported.
- Windows App SDK 2.5.1 is required.
- In a multi-targeted project (for example an Uno Platform project), the Windows target (`net*-windows10.0.*`) must be the active target framework of the project that contains the XAML file. Uno.Sdk projects select the Windows App SDK version with the `WinAppSdkVersion` property.
- The same goes for a multi-targeted library that the project references. Visual Studio gives the designer the build of the library for the target that is selected for it, and the controls of a build for another platform are not WinUI controls: the designer then reports `XDG0048` and `XDG0012` errors for every element of such a control and shows "Invalid Markup". With Uno.Sdk the selection is the `_UnoSelectedTargetFramework` property of the `.csproj.user` file of the library; set it to the Windows target, and reload the solution.

## Architecture

See [Architecture](https://github.com/0x5bfa/WinUIDesigner/blob/main/docs/architecture.md) for the designer's processing flow and implementation details.
