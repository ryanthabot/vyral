# Service Interfaces Reference

## Overview
Complete listing of all service interfaces in the application.

## Core Interfaces (VyralBot.Core)

### EventBus
| Interface | File | Key Methods |
|---|---|---|
| `IEventBus` | `src/VyralBot.Core/Services/EventBus/IEventBus.cs` | `PublishAsync<T>`, `SubscribeAsync<T>` → Guid, `Unsubscribe(Guid)`, `SubscriptionCount` |
| `IEvent` | `src/VyralBot.Core/Services/EventBus/IEvent.cs` | `EventId`, `Timestamp`, `Source`, `Metadata` |

### Data Access
| Interface | File | Key Methods |
|---|---|---|
| `IDataService` | `src/VyralBot.Data/IDataService.cs` | 80+ methods across 17 categories (Platform, Config, Trigger, Action, EventLog, WebServer, Chat, Currency, Command, AutoReply, Giveaway, Poll, Watchtime, Variable) |

### Profiles
| Interface | File | Key Methods |
|---|---|---|
| `IProfileService` | `src/VyralBot.Core/Services/Profiles/IProfileService.cs` | `GetCurrentProfileName`, `GetCurrentProfileAsync`, `GetProfilesAsync`, `CreateProfileAsync`, `SwitchProfileAsync`, `DeleteProfileAsync`, `ValidatePinAsync`, `UpdateProfilePinAsync`, `ExportProfileAsync`, `ImportProfileAsync` |

### Action/Trigger
| Interface | File | Key Methods |
|---|---|---|
| `IActionDispatcher` | `src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs` | `ExecuteActionAsync`, `ExecuteSequenceAsync`, `CancelTriggerActions`, `ActiveActionCount` |
| `ITriggerEngine` | `src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs` | `StartAsync`, `StopAsync`, `FireTriggerAsync`, `ReloadTriggersAsync`, `ActiveTriggerCount` |

### Chat
| Interface | File | Key Methods |
|---|---|---|
| `IChatMessageStore` | `src/VyralBot.Core/Services/Chat/IChatMessageStore.cs` | `AddAsync`, `GetRecentAsync`, `DeleteAsync` |
| `IChatEventSettingsService` | `src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs` | `GetSettingAsync`, `GetAllSettingsAsync`, `SaveSettingAsync`, `IsEventEnabledAsync`, `IsAutoReplyEnabledAsync`, `InvalidateCache` |
| `IAutoReplyService` | `src/VyralBot.Core/Services/Chat/IAutoReplyService.cs` | `ProcessTemplate`, `ShouldSendReply`, `GetAutoReplyAsync`, `CleanupExpiredEntries`, `ClearRateLimits` |

### Commands
| Interface | File | Key Methods |
|---|---|---|
| `ICommandService` | `src/VyralBot.Core/Services/Commands/ICommandService.cs` | `RegisterCommandAsync`, `ExecuteAsync`, `GetAllCommandsAsync`, `IsCommand`, `HasPermission`, `CheckCooldownAsync`, `ParseVariables` |

### Currency
| Interface | File | Key Methods |
|---|---|---|
| `ICurrencyService` | `src/VyralBot.Core/Services/Currency/ICurrencyService.cs` | `GetSettingsAsync`, `GetBalanceAsync`, `AddCurrencyAsync`, `RemoveCurrencyAsync`, `TransferCurrencyAsync`, `GetLeaderboardAsync`, `AwardChatActivityAsync`, `AwardWatchTimeAsync`, `AwardEventBonusAsync` |
| `ICurrencyCommandService` | `src/VyralBot.Core/Services/Currency/ICurrencyCommandService.cs` | `ProcessCommandAsync` |
| `ICurrencyRewardsService` | `src/VyralBot.Core/Services/Currency/ICurrencyRewardsService.cs` | Reward CRUD, `RedeemRewardAsync`, `ApproveRedemptionAsync`, `RejectRedemptionAsync` |

### Audio
| Interface | File | Key Methods |
|---|---|---|
| `IAudioWebSocketServer` | `src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs` | `StartAsync`, `StopAsync`, `SendAudioEventAsync`, `IsRunning`, `ConnectedClients` |
| `ISoundService` | `src/VyralBot.Core/Services/Audio/ISoundService.cs` | `GetSoundFilesAsync`, `UploadSoundAsync`, `DeleteSoundAsync`, `PlayEventSoundAsync`, `PlaySoundFileAsync`, `GetSoundsDirectory`, `InitializeDefaultSoundsAsync` |

### Variables
| Interface | File | Key Methods |
|---|---|---|
| `IVariableReplacementService` | `src/VyralBot.Core/Services/Variables/IVariableReplacementService.cs` | `ReplaceVariablesAsync`, `GetAvailableVariablesAsync`, `GetVariableValueAsync`, `ValidateVariableName`, `InvalidateCacheAsync` |

### Rules
| Interface | File | Key Methods |
|---|---|---|
| `IRuleEvaluator` | `src/VyralBot.Core/Services/Rules/RuleEvaluator.cs` | `EvaluateCondition(RuleCondition, Dictionary<string,object>)` |

### Statistics
| Interface | File | Key Methods |
|---|---|---|
| `IStatisticsService` | `src/VyralBot.Core/Services/Statistics/StatisticsService.cs` | `GetEventCountsByTypeAsync`, `GetTopContributorsAsync`, `GetTopUsersAsync`, `ExportToCsvAsync`, `GetUserStatisticsAsync` |

## Integration Interfaces (VyralBot.Integrations)

| Interface | File | Key Methods |
|---|---|---|
| `ITwitchService` | `src/VyralBot.Integrations/Twitch/ITwitchService.cs` | `ConnectAsync` (2 overloads), `DisconnectAsync`, `SendMessageAsync`, `IsConnected`, `Channel`, `BotUsername` |
| `ITwitchEventSubService` | `src/VyralBot.Integrations/Twitch/ITwitchEventSubService.cs` | `ConnectAsync`, `DisconnectAsync`, `IDisposable` |
| `IKickService` | `src/VyralBot.Integrations/Kick/IKickService.cs` | `ConnectAsync`, `DisconnectAsync`, `SendMessageAsync`, `IsConnected`, `Channel`, `BotUsername` |
