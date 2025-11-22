# AVAMP Udon Scripts

This folder contains the UdonSharp scripts required to display AVAMP supporter data in your VRChat world.

## Getting Started

1.  Ensure you have the [VRChat SDK3](https://vrchat.com/home/download) and [UdonSharp](https://github.com/MerlinVR/UdonSharp) installed in your Unity project.
2.  Import `AvampSupporterBoard.cs` into your project.
3.  Create a new UdonBehaviour on a GameObject (e.g., a Canvas or a Cube).
4.  Assign `AvampSupporterBoard` as the program source.
5.  Configure the `Data Url` with your GitHub Pages URL (e.g., `https://yourname.github.io/avamp-supporters/data.json`).

## Requirements

-   TextMeshPro (imported via Window > TextMeshPro > Import TMP Essentials)
-   VRChat SDK3 Worlds
-   UdonSharp

## Configuration

-   **Data URL**: The raw JSON URL from your AVAMP-connected GitHub repository.
-   **Refresh Interval**: How often to check for new supporters (default: 300s / 5 mins).
-   **Names Per Page**: Number of supporters to show per "slide".
-   **Page Display Time**: Seconds to wait before scrolling to the next page.
-   **Entry Format**: Customize how names look. Supports standard Rich Text.
    -   `{0}` = Supporter Name
    -   `{1}` = Tier Name
    -   Example: `{0} <color=yellow>[{1}]</color>`

## Features

-   **Auto-Scroll**: Automatically cycles through pages of supporters.
-   **Interact**: Players can click/interact with the board to manually advance pages or retry a failed sync.
-   **Rich Text**: Full support for TextMeshPro rich text tags (colors, sizes, fonts).
-   **Optimized**: Pre-calculates text to ensure zero lag when switching pages.
-   **Auto-Recovery**: Automatically retries connections if the internet fails.
