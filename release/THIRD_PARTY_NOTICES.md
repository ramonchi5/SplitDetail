# Third-Party Notices

## LiveSplit

SplitDetail is a custom component for LiveSplit and references LiveSplit APIs.

Some implementation patterns were developed with reference to LiveSplit and existing LiveSplit components, including component structure, comparison handling, layout settings integration, and rendering behavior.

LiveSplit is licensed under the MIT License.

Copyright (c) 2013 Christopher Serr and Sergey Papushin.

Official repository: https://github.com/LiveSplit/LiveSplit  
License: https://github.com/LiveSplit/LiveSplit/blob/master/LICENSE

## Alternative Timer

SplitDetail is designed to pair well with the separate `LiveSplit.AlternativeTimer.dll` component.

Alternative Timer repository: https://github.com/ramonchi5/AlternativeTimer

Alternative Timer is based on LiveSplit's Detailed Timer, removes the segment timer / segment comparison area, keeps the main run timer and current split name, and can display a leading segment counter from names such as `1/5 Gourd`.

LiveSplit and LiveSplit.DetailedTimer are licensed under the MIT License.

Copyright (c) 2013 Christopher Serr and Sergey Papushin.

Alternative Timer is not an official LiveSplit release and is not distributed inside the SplitDetail release package.

## LiveSplit.Core.dll

This project may reference `LiveSplit.Core.dll` for building against LiveSplit.

If `LiveSplit.Core.dll` is included in this repository or distributed with this project, it is covered by LiveSplit's MIT License notice above.

## UpdateManager.dll

This project references `UpdateManager.dll` during development/building.

The copy in `packages/` was taken from LiveSplit 1.8.29 for compile-time reference only. It is not required in the SplitDetail release package because LiveSplit provides it at runtime.

LiveSplit and its bundled assemblies are covered by the LiveSplit MIT License notice above.

## Microsoft .NET Framework Reference Assemblies

This project may use Microsoft .NET Framework Reference Assemblies for local builds.

The checked-in NuGet package metadata lists Microsoft as the author and links its license at https://github.com/Microsoft/dotnet/blob/master/LICENSE. These assemblies are build-time references and are not part of the SplitDetail release package.
