# Converters Quick Reference

## Overview
Complete listing of all 19 IValueConverter implementations.

## Converter Categories

### Boolean Converters
| Converter | File | Input → Output |
|-----------|------|----------------|
| `InvertBooleanConverter` | `InvertBooleanConverter.cs` | `bool` → inverted `bool` |
| `BoolToStringConverter` | `BoolToStringConverter.cs` | `bool` → configurable string |
| `BoolToStatusConverter` | `BoolToStatusConverter.cs` | `bool` → "Connected"/"Disconnected" |
| `BoolToExpandIconConverter` | `BoolToExpandIconConverter.cs` | `bool` → expand/collapse icon |

### Visibility Converters
| Converter | File | Input → Output |
|-----------|------|----------------|
| `InvertBooleanToVisibilityConverter` | `InvertBooleanToVisibilityConverter.cs` | `bool` → inverted `Visibility` |
| `InvertedBooleanToVisibilityConverter` | `InvertedBooleanToVisibilityConverter.cs` | Alternative inverted visibility |
| `EqualsToVisibilityConverter` | `EqualsToVisibilityConverter.cs` | Value equality → `Visibility` |
| `NullOrEmptyToVisibilityConverter` | `NullOrEmptyToVisibilityConverter.cs` | Null/empty → `Visibility` |
| `StringNullOrEmptyConverter` | `StringNullOrEmptyConverter.cs` | String check → bool/Visibility |

### Platform Converters
| Converter | File | Input → Output |
|-----------|------|----------------|
| `PlatformToBrushConverter` | `PlatformToBrushConverter.cs` | Platform name → `SolidColorBrush` |
| `PlatformToImageConverter` | `PlatformToImageConverter.cs` | Platform name → logo `ImageSource` |
| `BadgesToImagesConverter` | `BadgesToImagesConverter.cs` | Badge dictionary → badge images |

### Domain Converters
| Converter | File | Input → Output |
|-----------|------|----------------|
| `CurrencyConverters` | `CurrencyConverters.cs` | Currency display formatting |
| `EventHistoryConverters` | `EventHistoryConverters.cs` | Event history display |
| `ProfileConverters` | `ProfileConverters.cs` | Profile display formatting |
| `SetupWizardConverters` | `SetupWizardConverters.cs` | Setup wizard steps |
| `StatisticsConverters` | `StatisticsConverters.cs` | Statistics display |
| `CommandEditTitleConverter` | `CommandEditTitleConverter.cs` | Command edit title |
| `WatchtimeFormatConverter` | `WatchtimeFormatConverter.cs` | Minutes → "Xh Ym" |

## Directory
All files in `src/VyralBot.UI/Converters/`
