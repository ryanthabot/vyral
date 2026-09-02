# Contributing to VyralBot

Thank you for your interest in contributing! This document provides guidelines and best practices.

## Code of Conduct

This project adheres to a Code of Conduct. By participating, you agree to uphold this code.

## How to Contribute

### Reporting Bugs

1. **Search existing issues** to avoid duplicates
2. **Create a new issue** with:
   - Clear title and description
   - Steps to reproduce
   - Expected vs actual behavior
   - Environment details (.NET version, OS, etc.)
   - Logs or screenshots if applicable

### Suggesting Features

1. **Check roadmap** in README.md
2. **Open a discussion** in GitHub Discussions
3. Describe the feature, use cases, and benefits
4. Be open to feedback and alternative approaches

### Pull Requests

1. **Fork the repository**
2. **Create a feature branch**: `git checkout -b feature/my-feature`
3. **Follow coding standards** (see below)
4. **Write tests** for new functionality
5. **Update documentation** as needed
6. **Commit with clear messages**:
   - Use present tense ("Add feature" not "Added feature")
   - Reference issues: "Fix #123: Resolve OBS connection timeout"
7. **Push to your fork**: `git push origin feature/my-feature`
8. **Open a Pull Request** with:
   - Clear description of changes
   - Link to related issues
   - Screenshots/videos for UI changes

## Development Guidelines

### Coding Standards

- **C# Style**: Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- **Naming**:
  - PascalCase for public members, types
  - camelCase for private fields (prefix `_`)
  - Interfaces start with `I`
- **Async**: Use `async`/`await` consistently; suffix async methods with `Async`
- **Nullability**: Enable nullable reference types; use `?` and null-checks
- **Documentation**: Add XML comments for public APIs

### Architecture Principles

- **Clean Architecture**: Keep domain logic separate from infrastructure
- **SOLID**: Single responsibility, dependency inversion, etc.
- **EventBus**: Use for cross-module communication
- **Async First**: Non-blocking I/O throughout

### Testing

- **Unit Tests**: xUnit, Moq, FluentAssertions
- **Coverage**: Aim for >80% on core logic
- **Integration Tests**: Mock external services (OBS, Twitch, etc.)
- **Test Names**: `MethodName_Should_ExpectedBehavior_When_Condition`

Example:
```csharp
[Fact]
public async Task EventBus_Should_Deliver_Event_When_Published()
{
    // Arrange
    var eventBus = new InMemoryEventBus(logger);
    
    // Act
    await eventBus.PublishAsync(new TestEvent());
    
    // Assert
    receivedEvents.Should().HaveCount(1);
}
```

### Performance

- **Avoid blocking calls**: Use async/await
- **Object pooling**: For high-frequency allocations
- **Channels**: For backpressure-aware queues
- **Dispose properly**: Implement IDisposable/IAsyncDisposable

### Security

- **No hardcoded secrets**: Use configuration or environment variables
- **Encrypt tokens**: Use DPAPI or DataProtection APIs
- **Validate inputs**: Especially from scripts/plugins
- **Localhost default**: Network services bind to 127.0.0.1 by default

## Project-Specific Conventions

### EventBus

- Events are **immutable records** inheriting `BaseEvent`
- Event names: `source.action` (e.g., `obs.scene_changed`)
- Handlers must not throw; exceptions are logged

### Plugins

- Must implement `IPlugin`
- Run in isolated `AssemblyLoadContext`
- Access host via `IHostServices` only
- Permissions declared in `plugin.json`

### Adapters

- Implement async connect/disconnect
- Publish events via `IEventBus`
- Handle reconnection gracefully
- Log at appropriate levels

## Review Process

1. **Automated checks**: CI runs tests and linting
2. **Code review**: Maintainer reviews for quality, style, architecture
3. **Feedback**: Address comments; discussion encouraged
4. **Approval**: Maintainer approves and merges

## Commit Message Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types**: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`

**Example**:
```
feat(obs): Add support for OBS WebSocket authentication

Implements SHA256-based authentication for OBS WebSocket v5 protocol.
Adds password configuration to OBSConfig.

Closes #42
```

## Branch Naming

- `feature/short-description`
- `fix/issue-number-description`
- `docs/topic`
- `refactor/component-name`

## Questions?

- **GitHub Discussions** for general questions
- **GitHub Issues** for bug reports
- **Pull Requests** for code contributions

Thank you for contributing to VyralBot! 🎉
