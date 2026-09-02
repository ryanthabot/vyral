# Settings View Redesign Plan

## Overview
Complete redesign of SettingsView.xaml to match the Home view's category-based navigation pattern.

## File Structure

### UserControl Header
```xml
<UserControl x:Class="VyralBot.UI.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
             xmlns:converters="clr-namespace:VyralBot.UI.Converters"
             Background="{DynamicResource BackgroundPrimaryBrush}">
```

### Resources Section
- BooleanToVisibilityConverter
- StringToVisibilityConverter

### Layout Structure
- Main Grid with 2 rows:
  - Row 0: Header ("Settings" title)
  - Row 1: Content grid with 2 columns
    - Column 0: Category Navigation (280px width)
    - Column 1: Content Area (remaining space)

## Left Sidebar - Category Navigation (Column 0)

Border with ScrollViewer containing 11 RadioButton categories:

1. **Profile**
   - Icon: Account
   - Command: SelectCategoryCommand
   - Parameter: "Profile"

2. **Theme**
   - Icon: Palette
   - Command: SelectCategoryCommand
   - Parameter: "Theme"

3. **Data Management**
   - Icon: Database
   - Command: SelectCategoryCommand
   - Parameter: "Data"

4. **Notifications**
   - Icon: Bell
   - Command: SelectCategoryCommand
   - Parameter: "Notifications"

5. **Commands**
   - Icon: CodeBraces
   - Command: SelectCategoryCommand
   - Parameter: "Commands"

6. **Timers**
   - Icon: ClockOutline
   - Command: SelectCategoryCommand
   - Parameter: "Timers"

7. **Automation**
   - Icon: Robot
   - Command: SelectCategoryCommand
   - Parameter: "Automation"

8. **API & Webhooks**
   - Icon: Api
   - Command: SelectCategoryCommand
   - Parameter: "API"

9. **Performance**
   - Icon: Speedometer
   - Command: SelectCategoryCommand
   - Parameter: "Performance"

10. **Security**
    - Icon: Security
    - Command: SelectCategoryCommand
    - Parameter: "Security"

11. **Advanced**
    - Icon: Cog
    - Command: SelectCategoryCommand
    - Parameter: "Advanced"

## Right Content Area (Column 1)

Border with ScrollViewer containing all content panels. Each panel uses visibility binding based on `SelectedCategory` property.

### 1. Profile Panel
**Visibility:** `{Binding SelectedCategory, Converter={StaticResource EqualsToVisibilityConverter}, ConverterParameter=Profile}`

**Content:**
- Status message banner
- Current Profile section
  - Display name and username
  - Change PIN button
- Auto-Login toggle with save button
- All Profiles section
  - New Profile button
  - Profiles list with:
    - Switch button
    - Export button
    - Delete button (disabled for current profile)

### 2. Theme Panel
**Visibility:** Based on SelectedCategory = "Theme"

**Content:**
- Theme selector ComboBox
  - RedSlate (current)
  - Dark
  - Light
  - Christmas
  - Halloween
  - Custom
- Color preview grid showing:
  - Primary color
  - Background colors
  - Text colors
  - Platform colors
- Apply Theme button
- Save as Custom button
- Reset to Default button

### 3. Data Management Panel
**Visibility:** Based on SelectedCategory = "Data"

**Content:**
- Database Information
  - Current database size
  - Last backup date
  - Storage location
- Clear Data section
  - Clear Chat History button (with count)
  - Clear Command History button
  - Clear Event Logs button
  - Clear All Data button (with confirmation)
- Import/Export section
  - Export Profile Data button
  - Import Profile Data button
  - Auto-backup toggle
  - Backup frequency selector
- Reset section
  - Reset Settings to Default button
  - Reset All Data button (dangerous)

### 4. Notifications Panel
**Visibility:** Based on SelectedCategory = "Notifications"

**Content:**
- Desktop Notifications
  - Enable desktop notifications toggle
  - Notification duration slider
- Sound Settings
  - Enable sound alerts toggle
  - Master volume slider
  - Test sound button
- Event Notifications (toggles for each)
  - New follower
  - New subscriber
  - Gift subs
  - Bits/tips
  - Raids
  - Hosts
  - First-time chatters
- Custom Sound Files
  - Follow sound file picker
  - Sub sound file picker
  - Raid sound file picker
  - Default sound restore button

### 5. Commands Panel
**Visibility:** Based on SelectedCategory = "Commands"

**Content:**
- Command Settings
  - Command prefix input (e.g., "!")
  - Enable commands toggle
- Cooldown Settings
  - Global cooldown (seconds)
  - Per-user cooldown (seconds)
  - Moderator bypass toggle
  - Subscriber cooldown reduction
- Permission Levels
  - Everyone
  - Subscribers
  - VIPs
  - Moderators
  - Broadcaster only
- Built-in Commands
  - Enable/disable toggles for:
    - !commands
    - !uptime
    - !followage
    - !watchtime
    - !title
    - !game
    - !socials
- Custom Commands section
  - Add Command button
  - Commands list (placeholder)

### 6. Timers Panel
**Visibility:** Based on SelectedCategory = "Timers"

**Content:**
- Timer Settings
  - Enable timers toggle
  - Minimum chat lines between timers
  - Minimum time between timers (minutes)
  - Shuffle timers toggle
  - Only run when live toggle
- Active Timers
  - Add Timer button
  - Timers list (placeholder)
    - Timer name
    - Interval
    - Message
    - Enabled toggle
    - Edit/Delete buttons

### 7. Automation Panel
**Visibility:** Based on SelectedCategory = "Automation"

**Content:**
- Auto-Moderation
  - Block excessive caps toggle + threshold slider
  - Block links toggle (with whitelist)
  - Block spam toggle
  - Timeout duration selector
  - Permit command toggle
- Auto-Responses
  - Auto-welcome new chatters toggle
  - Welcome message input
  - Auto-shoutout raiders toggle
  - Shoutout message template
- Custom Automation Rules
  - Add Rule button
  - Rules list (placeholder)
    - Trigger type
    - Condition
    - Action
    - Edit/Delete buttons

### 8. API & Webhooks Panel
**Visibility:** Based on SelectedCategory = "API"

**Content:**
- Webhook Configuration
  - Webhook URL input
  - Secret key input
  - Test Webhook button
  - Webhook events checkboxes:
    - Follow
    - Subscribe
    - Raid
    - Bits
    - Chat message
- API Settings
  - Enable API server toggle
  - API port input
  - API key display + regenerate button
- Rate Limiting
  - Twitch API rate limit display
  - Kick API rate limit display
  - YouTube API quota display
- Custom Endpoints
  - Add Endpoint button
  - Endpoints list (placeholder)

### 9. Performance Panel
**Visibility:** Based on SelectedCategory = "Performance"

**Content:**
- Memory & Resources
  - Current memory usage display
  - CPU usage display
  - Clear cache button
  - Cache size display
- Connection Settings
  - Chat message buffer size slider
  - Max reconnection attempts
  - Reconnection delay (seconds)
  - WebSocket timeout (seconds)
- Optimization
  - Enable hardware acceleration toggle
  - Reduce animations toggle
  - Limit chat history (messages count)
  - Auto-cleanup old data toggle
- Diagnostics
  - Export performance log button
  - Run diagnostics button

### 10. Security Panel
**Visibility:** Based on SelectedCategory = "Security"

**Content:**
- Authentication
  - Enable two-factor authentication toggle
  - Require PIN on startup toggle
  - Session timeout selector (minutes)
  - Lock on minimize toggle
- Trusted Devices
  - Current device fingerprint display
  - Trusted devices list (placeholder)
  - Revoke all sessions button
- Privacy
  - Anonymize logs toggle
  - Send anonymous usage data toggle
  - Clear sensitive data on exit toggle
- Security Audit Log
  - Last 10 security events list
  - View full log button
  - Export audit log button

### 11. Advanced Panel
**Visibility:** Based on SelectedCategory = "Advanced"

**Content:**
- Developer Settings
  - Enable debug mode toggle
  - Enable developer mode toggle
  - Show FPS counter toggle
  - Log level selector (Info/Debug/Trace)
- Paths & Locations
  - Application data path display + open button
  - Database path display + open button
  - Logs path display + open button
  - Change data location button
- Maintenance
  - Export all logs button
  - Vacuum database button
  - Rebuild database button
  - Check for updates button
- Danger Zone
  - Reset application button
  - Reinstall application button
  - Delete all data button (with multiple confirmations)

## Modal Overlays (Outside Main Grid)

All modals positioned absolutely over the entire view:

### 1. Delete Profile Confirmation Modal
- Visibility: `{Binding ShowDeleteConfirmation}`
- Content: Warning message, Cancel/Delete buttons

### 2. Change PIN Modal
- Visibility: `{Binding ShowChangePinDialog}`
- Content: Current PIN, New PIN, Confirm PIN inputs, Cancel/Save buttons

### 3. Create Profile Modal
- Visibility: `{Binding ShowCreateProfileDialog}`
- Content: Profile name, display name, PIN inputs, Cancel/Create buttons

### 4. Switch Profile Modal
- Visibility: `{Binding ShowSwitchProfileDialog}`
- Content: Profile display name, PIN input, Cancel/Switch buttons

## ViewModel Updates Required

### New Properties
```csharp
[ObservableProperty]
private string _selectedCategory = "Profile"; // Default to Profile
```

### New Commands
```csharp
[RelayCommand]
private void SelectCategory(string category)
{
    SelectedCategory = category;
    _logger.LogInformation("Selected category: {Category}", category);
}
```

### New Converter Needed
Create `EqualsToVisibilityConverter`:
```csharp
// Returns Visible if value equals parameter, Collapsed otherwise
```

### Placeholder Properties for New Settings
- Theme settings (selected theme, custom colors)
- Notification settings (all toggle states, sound files)
- Command settings (prefix, cooldowns, permissions)
- Timer settings (intervals, enabled state)
- Automation settings (all toggle states, rules)
- API settings (webhook URL, endpoints)
- Performance settings (buffer sizes, limits)
- Security settings (2FA, session timeout, trusted devices)
- Advanced settings (debug mode, paths)

## Implementation Notes

1. **Profile content** - Move all existing profile management code from old SettingsView into Profile panel
2. **Other panels** - Create with placeholder content and "Coming Soon" text or basic UI elements
3. **Visibility binding** - Each panel checks if `SelectedCategory` matches its category name
4. **Styling** - Use MaterialDesign cards, proper spacing, theme colors
5. **Icons** - Use MaterialDesign PackIcons for all buttons and categories
6. **Responsive** - Ensure ScrollViewer on both sidebar and content for overflow handling
7. **Default state** - Profile category selected by default on load

## Testing Checklist

- [ ] All 11 category buttons visible and clickable
- [ ] Only one category content visible at a time
- [ ] Profile panel shows all existing functionality
- [ ] All modals still work (delete, change PIN, create, switch)
- [ ] Theme colors properly applied throughout
- [ ] No binding errors in output
- [ ] Smooth scrolling in both sidebar and content
- [ ] Proper spacing and alignment
- [ ] Icons render correctly
- [ ] Status messages display properly
