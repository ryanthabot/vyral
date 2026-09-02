using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VyralBot.Core.Services.Commands;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class CommandsViewModel : ViewModelBase
{
    private readonly ILogger<CommandsViewModel> _logger;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private ObservableCollection<Command> _builtInCommands = new();

    [ObservableProperty]
    private ObservableCollection<Command> _customCommands = new();

    [ObservableProperty]
    private Command? _selectedCommand;

    [ObservableProperty]
    private bool _isEditingCommand;

    [ObservableProperty]
    private string _commandName = "";

    [ObservableProperty]
    private string _commandResponse = "";

    [ObservableProperty]
    private string _selectedPermission = "Everyone";

    [ObservableProperty]
    private int _globalCooldown = 5;

    [ObservableProperty]
    private int _userCooldown = 30;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _enabledOnKick = true;

    [ObservableProperty]
    private bool _enabledOnTwitch = true;

    [ObservableProperty]
    private string _statusMessage = "";

    public ObservableCollection<string> PermissionLevels { get; } = new()
    {
        "Everyone",
        "Subscriber",
        "VIP",
        "Moderator",
        "Broadcaster"
    };

    public CommandsViewModel(
        ILogger<CommandsViewModel> logger,
        ICommandService commandService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadCommandsAsync();
            _logger.LogInformation("CommandsViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CommandsViewModel");
            StatusMessage = "Failed to load commands";
        }
    }

    [RelayCommand]
    private async Task LoadCommandsAsync()
    {
        try
        {
            var allCommands = await _commandService.GetAllCommandsAsync();
            
            BuiltInCommands.Clear();
            CustomCommands.Clear();

            foreach (var command in allCommands)
            {
                if (command.IsBuiltIn)
                    BuiltInCommands.Add(command);
                else
                    CustomCommands.Add(command);
            }

            _logger.LogInformation("Loaded {BuiltInCount} built-in and {CustomCount} custom commands",
                BuiltInCommands.Count, CustomCommands.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load commands");
            StatusMessage = "Failed to load commands";
        }
    }

    [RelayCommand]
    private void StartAddCommand()
    {
        IsEditingCommand = true;
        SelectedCommand = null;
        CommandName = "";
        CommandResponse = "";
        SelectedPermission = "Everyone";
        GlobalCooldown = 5;
        UserCooldown = 30;
        IsEnabled = true;
        EnabledOnKick = true;
        EnabledOnTwitch = true;
        StatusMessage = "";
    }

    [RelayCommand]
    private void StartEditCommand(Command? command)
    {
        if (command == null || command.IsBuiltIn)
        {
            StatusMessage = "Cannot edit built-in commands";
            return;
        }

        IsEditingCommand = true;
        SelectedCommand = command;
        CommandName = command.Name;
        CommandResponse = command.Response;
        SelectedPermission = command.Permission;
        GlobalCooldown = command.GlobalCooldown;
        UserCooldown = command.UserCooldown;
        IsEnabled = command.IsEnabled;
        EnabledOnKick = command.EnabledOnKick;
        EnabledOnTwitch = command.EnabledOnTwitch;
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task SaveCommandAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CommandName))
            {
                StatusMessage = "Command name cannot be empty";
                return;
            }

            if (!CommandName.StartsWith("!") && !CommandName.StartsWith("?") && !CommandName.StartsWith("."))
            {
                StatusMessage = "Command name must start with !, ?, or .";
                return;
            }

            if (string.IsNullOrWhiteSpace(CommandResponse))
            {
                StatusMessage = "Command response cannot be empty";
                return;
            }

            var command = new Command
            {
                Id = SelectedCommand?.Id ?? Guid.NewGuid().ToString(),
                Name = CommandName.ToLowerInvariant(),
                Response = CommandResponse,
                Permission = SelectedPermission,
                GlobalCooldown = GlobalCooldown,
                UserCooldown = UserCooldown,
                IsEnabled = IsEnabled,
                IsBuiltIn = false,
                EnabledOnKick = EnabledOnKick,
                EnabledOnTwitch = EnabledOnTwitch,
                UsageCount = SelectedCommand?.UsageCount ?? 0,
                LastUsedAt = SelectedCommand?.LastUsedAt,
                CreatedAt = SelectedCommand?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (SelectedCommand == null)
            {
                // Adding new command
                await _commandService.RegisterCommandAsync(command);
                StatusMessage = $"Command {CommandName} added successfully";
                _logger.LogInformation("Added new command: {CommandName}", CommandName);
            }
            else
            {
                // Updating existing command
                await _commandService.UpdateCommandAsync(command);
                StatusMessage = $"Command {CommandName} updated successfully";
                _logger.LogInformation("Updated command: {CommandName}", CommandName);
            }

            await LoadCommandsAsync();
            CancelEdit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save command: {CommandName}", CommandName);
            StatusMessage = $"Failed to save command: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingCommand = false;
        SelectedCommand = null;
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task DeleteCommandAsync(Command? command)
    {
        if (command == null)
        {
            StatusMessage = "No command selected";
            return;
        }

        if (command.IsBuiltIn)
        {
            StatusMessage = "Cannot delete built-in commands";
            return;
        }

        try
        {
            await _commandService.DeleteCommandAsync(command.Id);
            StatusMessage = $"Command {command.Name} deleted successfully";
            _logger.LogInformation("Deleted command: {CommandName}", command.Name);
            await LoadCommandsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete command: {CommandName}", command.Name);
            StatusMessage = $"Failed to delete command: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleCommandAsync(Command? command)
    {
        if (command == null) return;

        try
        {
            command.IsEnabled = !command.IsEnabled;
            command.UpdatedAt = DateTime.UtcNow;
            await _commandService.UpdateCommandAsync(command);
            
            StatusMessage = $"Command {command.Name} {(command.IsEnabled ? "enabled" : "disabled")}";
            _logger.LogInformation("Toggled command {CommandName} to {Status}", 
                command.Name, command.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle command: {CommandName}", command.Name);
            StatusMessage = $"Failed to toggle command: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TogglePlatformAsync(Command? command)
    {
        if (command == null) return;

        try
        {
            // Note: CheckBox IsChecked binding already toggled the value
            command.UpdatedAt = DateTime.UtcNow;
            await _commandService.UpdateCommandAsync(command);

            StatusMessage = $"Updated {command.Name} platform settings";
            _logger.LogInformation("Updated platform settings for command {CommandName}", command.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle platform for command");
            StatusMessage = $"Failed to update platform settings: {ex.Message}";
        }
    }
}
