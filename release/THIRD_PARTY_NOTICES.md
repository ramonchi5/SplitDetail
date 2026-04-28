# Third-Party Notices

## LiveSplit

SplitDetail is a custom component for LiveSplit and references LiveSplit APIs.

Some implementation patterns were developed with reference to LiveSplit and existing LiveSplit components, including component structure, comparison handling, layout settings integration, and rendering behavior.

LiveSplit is licensed under the MIT License.

Copyright (c) 2013 Christopher Serr and Sergey Papushin.

Official repository: https://github.com/LiveSplit/LiveSplit  
License: https://github.com/LiveSplit/LiveSplit/blob/master/LICENSE

## Alternative Detailed Timer

Some SplitDetail releases may include `LiveSplit.AlternativeDetailedTimer.dll`, an unofficial modified build of LiveSplit's Detailed Timer component intended to pair with SplitDetail.

Original component repository: https://github.com/LiveSplit/LiveSplit.DetailedTimer

The modified build removes the segment timer / segment comparison area, keeps the main run timer and current split name, and changes the component identity to avoid presenting itself as the official Detailed Timer component.

LiveSplit and LiveSplit.DetailedTimer are licensed under the MIT License.

Copyright (c) 2013 Christopher Serr and Sergey Papushin.

This modified DLL is not an official LiveSplit release.

## LiveSplit.Core.dll

This project may reference `LiveSplit.Core.dll` for building against LiveSplit.

If `LiveSplit.Core.dll` is included in this repository or distributed with this project, it is covered by LiveSplit's MIT License notice above.

## UpdateManager.dll

This project may reference `UpdateManager.dll` during development/building.

Before publicly redistributing `UpdateManager.dll`, verify the exact source and license of the DLL included in this repository. If it is the GPL-licensed UpdateManager library, additional GPL obligations may apply.

If the DLL is not required to build or distribute SplitDetail, prefer removing it from the public repository and referencing the copy included with LiveSplit or your local build environment instead.

## Microsoft .NET Framework Reference Assemblies

This project may use Microsoft .NET Framework Reference Assemblies for building against .NET Framework 4.8.1.

These should ideally be restored through NuGet/build tooling rather than manually redistributed in this repository.
