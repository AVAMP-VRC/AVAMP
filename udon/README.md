# AVAMP UdonSharp Scripts

This folder contains the UdonSharp scripts for integrating AVAMP supporter data into your VRChat world.

## 📦 Available Scripts

### 1. AvampSupporterBoard.cs
Displays your supporters in a customizable in-world board with tier grouping, colors, and pagination.

### 2. AvampVipDoor.cs (v2.0)
Controls access to VIP areas by checking if a player is on your supporter list. Now with teleportation support!

### 3. VipDoorButton.cs
A relay script that allows multiple buttons to trigger the VIP door without rate-limiting issues.

---

## 🚀 Quick Start

### Requirements
- [VRChat SDK3 Worlds](https://vrchat.com/home/download)
- [UdonSharp](https://github.com/MerlinVR/UdonSharp)
- TextMeshPro (Window → TextMeshPro → Import TMP Essentials)

### Basic Setup
1. Import the `.cs` files into your Unity project (usually in `Assets/Udon/`)
2. Create or select a GameObject to attach the script to
3. Add an **Udon Behaviour** component
4. Assign the script as the **Program Source**
5. Configure the public fields in the Inspector

---

## 🎨 AvampSupporterBoard.cs

### What It Does
Fetches your supporter data from GitHub Pages and displays it in-world with automatic pagination, tier grouping, and customizable styling.

### Key Functions

#### `Start()`
- Called when the world loads
- Validates configuration (checks if URLs are set)
- Applies visual settings (colors, opacity, title)
- Initiates the first data load

#### `Update()`
- Runs every frame
- **Auto-Scroll Logic**: Cycles through pages after `pageDisplayTime` seconds
- **Auto-Refresh Logic**: Fetches updated data every `refreshInterval` seconds

#### `LoadData()`
- Randomly selects one of the 500 cache-busting URLs
- Downloads JSON data using VRCStringDownloader
- Sets loading state to prevent duplicate requests

#### `OnStringLoadSuccess(IVRCStringDownload result)`
- Called when data download succeeds
- Parses the JSON using VRCJson
- Extracts supporters, tiers, and colors
- Calls `BuildPages()` to format the text
- Updates the board display

#### `OnStringLoadError(IVRCStringDownload result)`
- Called when download fails
- Logs the error
- Schedules a retry after 10 seconds
- Shows "Connection Failed" status

#### `BuildPages()`
- Groups supporters by tier
- Calculates how many names fit per page (using Smart Page Sizing if enabled)
- Formats text with colors and tier headers
- Creates paginated text strings
- Handles both List and Grid layout modes

#### `CalculateSmartPageSize()`
- Calculates how many names fit on the board based on:
  - ContentText RectTransform height
  - `contentFontSize` value (must match Unity's font size)
  - 1.25x line height multiplier
- Returns the calculated lines per page

#### `ShowPage(int pageIndex)`
- Updates ContentText with the specified page
- Updates StatusText with page counter (e.g., "Page 2 of 5")

#### `Interact()`
- Called when a player clicks the board
- Advances to the next page
- If on the last page, wraps to page 1
- Resets the scroll timer

#### `ApplyVisuals()`
- Sets the board title
- Applies header and text colors
- Sets background opacity
- Updates all visual elements

#### `ValidateConfiguration()`
- Ensures Smart Page Sizing has a valid font size
- Checks if UI references are assigned
- Logs warnings for missing configuration

---

## 🚪 AvampVipDoor.cs (v2.0)

### What It Does
Checks if a player's VRChat display name is on your VIP list before granting access. Can hide/show objects, trigger visuals, and teleport players.

### Key Functions

#### `Start()`
- Gets the local player's display name
- Resets door to closed state
- Initiates the first VIP list download

#### `Update()`
- Runs every frame
- **Auto-Refresh Logic**: Downloads updated VIP lists every `refreshInterval` seconds (minimum 60s)

#### `Interact()`
- Called when a player clicks a VipDoorButton
- If player is cached as VIP, grants access immediately
- Otherwise, triggers `LoadData(true)` to verify access

#### `LoadData(bool didInteract)`
- Randomly selects one of the 500 cache-busting URLs
- Downloads VIP list using VRCStringDownloader
- Tracks whether this was triggered by player interaction

#### `OnStringLoadSuccess(IVRCStringDownload result)`
- Called when VIP list download succeeds
- Parses the JSON and checks if player is in the list
- Caches the result to prevent repeated downloads
- If triggered by interaction and player is VIP, calls `ExecuteAccessGranted()`
- If access denied, logs error message

#### `OnStringLoadError(IVRCStringDownload result)`
- Called when download fails
- Logs the error if debug mode is enabled
- Schedules a retry after 10 seconds

#### `CheckAccessInJSON(string json)`
- Parses the JSON using VRCJson
- Checks two arrays:
  - `allowed_users` (simple string array)
  - `supporters` (array of objects with "name" field)
- Compares player's display name (case-insensitive)
- Returns `true` if player is found, `false` otherwise

#### `ExecuteAccessGranted()`
- Hides `lockedDoorObject` (the blocking wall/door)
- Shows `unlockVisuals` (green light, particles, etc.)
- Teleports player to `teleportTarget` position and rotation (if set)
- Schedules `ResetDoorState()` after `closeDelay` seconds (if auto-close enabled)

#### `ResetDoorState()`
- Shows `lockedDoorObject` (closes the door)
- Hides `unlockVisuals` (turns off unlock effects)
- Called automatically after `closeDelay` if `useAutoClose` is true

#### `RetryLoad()`
- Simple retry function called after failed downloads
- Triggers `LoadData(false)` to try again

---

## 🔘 VipDoorButton.cs

### What It Does
A simple relay script that allows multiple buttons to trigger the same VIP door. This prevents rate-limiting issues when multiple players click buttons simultaneously.

### Key Functions

#### `Interact()`
- Called when a player clicks the button GameObject
- Calls `mainDoorScript.Interact()` to trigger the access check on the main AvampVipDoor script

### Setup
1. Add this script to any GameObject with a Collider
2. In the Inspector, drag your `VIP_System` GameObject (with AvampVipDoor script) into the **Main Door Script** field
3. Duplicate the button as many times as needed - they all reference the same "Brain"

---

## 🔗 Understanding the 500 Link Generation

### Why Do We Need 500 URLs?

VRChat aggressively caches external URLs. Without cache-busting, your board would show stale supporter data for hours or days, even after you update your list.

### How It Works

1. **You provide a base URL**:
   ```
   https://yourname.github.io/avamp-supporters/data.json
   ```

2. **The Generator creates 500 variations** by adding a query parameter:
   ```
   https://yourname.github.io/avamp-supporters/data.json?t=0
   https://yourname.github.io/avamp-supporters/data.json?t=1
   https://yourname.github.io/avamp-supporters/data.json?t=2
   ...
   https://yourname.github.io/avamp-supporters/data.json?t=499
   ```

3. **The script randomly picks one**:
   - Every time it refreshes, it randomly selects a URL from the array
   - VRChat sees each URL as unique, so it bypasses the cache
   - Your server (GitHub Pages) ignores the `?t=` parameter and returns the same JSON file

4. **Result**: Fresh data every refresh, even with VRChat's aggressive caching.

### How to Generate the Links

**In Unity:**
1. Paste your base URL into the `sourceUrl` field
2. Scroll to the bottom of the Inspector
3. Click the **⚡ GENERATE 500 LINKS ⚡** button (or **⚡ GENERATE VIP LINKS ⚡** for VIP Door)
4. The `dataUrls` (or `vipListUrls`) array will populate with 500 URLs

**CRITICAL**: You MUST run the generator after pasting your URL. The scripts will not work if the array is empty.

---

## ⚙️ Configuration Guide

### AvampSupporterBoard Configuration

#### Cache Buster Setup
- **`sourceUrl`**: Your GitHub Pages JSON URL (e.g., `https://yourname.github.io/avamp-supporters/data.json`)
- **`dataUrls`**: Auto-filled by the Generator. Contains 500 cache-busting URLs.

#### AVAMP Configuration
- **`refreshInterval`**: How often to check for updates (seconds). Default: 300 (5 minutes). Minimum: 60.

#### Visual Customization
- **`boardTitle`**: The header text. Default: "Our Supporters"
- **`headerColor`**: Color for tier headers and title
- **`textColor`**: Color for supporter names
- **`backgroundOpacity`**: Opacity of the background (0-1)

#### Smart Page Sizing
- **`contentFontSize`**: **CRITICAL** - Must match the Font Size of your ContentText in Unity
- **`smartPageSizing`**: If enabled, automatically calculates how many names fit per page

#### Layout Settings
- **`layoutMode`**: 0 = List (vertical), 1 = Grid (multi-column)
- **`gridColumns`**: Number of columns in Grid mode
- **`gridColumnSpacing`**: Horizontal spacing between columns (15-50%)
- **`namesPerPage`**: Manual override (only used if Smart Page Sizing is OFF)
- **`pageDisplayTime`**: Seconds to display each page before scrolling
- **`gridColumnWidth`**: Max characters per column for alignment

#### UI References
- **`contentText`**: TextMeshProUGUI for supporter names
- **`statusText`**: TextMeshProUGUI for status/page counter
- **`headerText`**: TextMeshProUGUI for the board title
- **`backgroundImage`**: Image component for the background

### AvampVipDoor Configuration

#### Cache Buster Setup
- **`sourceUrl`**: Your VIP list URL (e.g., `https://yourname.github.io/avamp-supporters/vip.json`)
- **`vipListUrls`**: Auto-filled by the Generator. Contains 500 cache-busting URLs.

#### AVAMP Configuration
- **`refreshInterval`**: How often to refresh the VIP list (seconds). Default: 300 (5 minutes).

#### Door Setup
- **`lockedDoorObject`**: The solid door/wall that DISAPPEARS when access is granted
- **`unlockVisuals`**: Optional effects (light, particles) that APPEAR when access is granted
- **`teleportTarget`**: Optional Transform. If set, VIPs are teleported here immediately.

#### Timers
- **`useAutoClose`**: If true, door resets after `closeDelay` seconds
- **`closeDelay`**: How long the door stays open (seconds). Default: 5.0

#### Debug
- **`debugMode`**: Enable console logging for testing

---

## 🧪 Testing Your Setup

### For Supporter Board
1. Enable Play Mode in Unity
2. Check the Status Text:
   - Should show "Loading..." then "Page 1 of X"
   - If it shows "Connection Failed", check your URLs
3. Watch the board auto-scroll through pages
4. Click the board to manually advance pages

### For VIP Door
1. Enable `debugMode` in the Inspector
2. Enable Play Mode in Unity
3. Click the button
4. Check the Console:
   - Should show "[VIP] Checking Access..."
   - Then either "[VIP] Access Granted." or "[VIP] Access Denied"
5. If using a test account, add your VRChat display name to the JSON file

---

## 🔧 Troubleshooting

### "Config Error: Run Generator"
- You forgot to click the GENERATE LINKS button
- Click it to populate the URL array

### "Connection Failed"
- Check your `sourceUrl` is correct
- Ensure GitHub Pages is deployed and accessible
- Try opening the URL in a browser to verify it works
- Check Unity Console for detailed error messages

### "Access Denied" (but I'm on the list)
- Enable `debugMode` and check Console logs
- Verify your VRChat display name matches exactly what's in the JSON
- Check the JSON file is properly formatted
- Ensure the `vipListUrls` array is populated (run Generator)

### Smart Page Sizing not working correctly
- Verify `contentFontSize` matches your ContentText font size in Unity exactly
- Check that `smartPageSizing` is enabled
- Ensure ContentText RectTransform height is set correctly

### VIP Door buttons not clickable
- Ensure button GameObject has a Collider
- Check Layer is set to Default (not IgnoreRaycast)
- Verify VipDoorButton script is attached
- Ensure `mainDoorScript` field is linked to your VIP_System

### Teleportation not working
- Verify `teleportTarget` field is not empty
- Check the target GameObject exists in the scene
- Lift target slightly above floor (Y: 0.1)
- Rotate target so Blue Arrow (Z-Axis) faces desired direction

---

## 📚 Additional Resources

- **Full Documentation**: [AVAMP Wiki](https://avamp.app/wiki)
- **Setup Guide**: [Getting Started](https://avamp.app/setup)
- **Unity Setup**: [Detailed Unity Instructions](https://avamp.app/wiki/unity-setup)
- **Scripts Reference**: [Complete Script Documentation](https://avamp.app/wiki/scripts)
- **Support**: [GitHub Issues](https://github.com/AVAMP-VRC/AVAMP/issues)

---

## 📝 Notes

- Both scripts use `BehaviourSyncMode.None` - they only run locally for each player
- Supporter data is fetched per-player, not synced across the instance
- JSON parsing uses VRChat's built-in VRCJson API (requires SDK 3.5.0+)
- The 500 URL limit is arbitrary but provides good cache-busting coverage
- VIP door checks names **case-insensitive** as of v2.0

---

## 🎉 Version History

### v2.0 (Current)
- **VIP Door**: Added teleportation support
- **VIP Door**: Multi-button relay system (VipDoorButton.cs)
- **VIP Door**: Improved object toggle logic (lockedDoorObject, unlockVisuals)
- **Board**: Smart Page Sizing feature
- **Board**: Grid layout mode
- **Both**: Enhanced cache busting with 500 URLs

### v1.0
- Initial release
- Basic supporter board with pagination
- VIP door with access control

---

Made with ❤️ by the AVAMP team
