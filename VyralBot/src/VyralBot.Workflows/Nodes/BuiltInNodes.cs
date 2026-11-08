namespace VyralBot.Workflows.Nodes;

/// <summary>
/// The node implementations shipped with the engine. The UI registers them at
/// startup: <c>BuiltInNodes.RegisterAll(nodeRegistry)</c>. Future plugins
/// register additional nodes through the same <see cref="NodeRegistry.Register"/> API.
/// </summary>
public static class BuiltInNodes
{
    /// <summary>Registers every built-in node into the given registry.</summary>
    public static void RegisterAll(NodeRegistry registry)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));

        // Messaging / actions.
        registry.Register(new SendMessageNode());
        registry.Register(new SendEmbedNode());
        registry.Register(new SendDmNode());
        registry.Register(new EditMessageNode());
        registry.Register(new DeleteMessageNode());
        registry.Register(new ReactNode());
        registry.Register(new AddRoleNode());
        registry.Register(new RemoveRoleNode());
        registry.Register(new CreateChannelNode());
        registry.Register(new CreateThreadNode());
        registry.Register(new LockChannelNode());
        registry.Register(new WaitResponseNode());
        registry.Register(new HttpRequestNode());
        registry.Register(new PlaySoundNode());

        // Command editor (legacy built-ins + moderation inside flows).
        registry.Register(new BuiltinCommandNode());
        registry.Register(new ModerateMemberNode());

        // Loops (the runtime interprets "loop" nodes natively).
        registry.Register(new LoopNode());
        registry.Register(new StopLoopNode());

        // Attachment cards (pass-through nodes the editor renders below loop and
        // comparison blocks; no-ops that keep graph walkers routing through them).
        registry.Register(new LoopAfterNode());
        registry.Register(new LoopBodyNode());
        registry.Register(new ComparisonCaseNode());
        registry.Register(new ComparisonElseNode());

        // Logic / conditions.
        registry.Register(new IfNode());
        registry.Register(new IfUserHasRoleNode());
        registry.Register(new IfMessageContainsNode());
        registry.Register(new IfVariableEqualsNode());
        registry.Register(new IfTimeBetweenNode());
        registry.Register(new IfUserIsBotNode());
        registry.Register(new ComparisonNode());
        registry.Register(new IfUserPermissionNode());
        registry.Register(new IfChannelNode());
        registry.Register(new IfUserNode());
        registry.Register(new SwitchNode());
        registry.Register(new ChanceNode());

        // Variables / economy / timers.
        registry.Register(new SetVariableNode());
        registry.Register(new RandomNumberNode());
        registry.Register(new AddCurrencyNode());
        registry.Register(new RemoveCurrencyNode());
        registry.Register(new WaitNode());
        registry.Register(new LogNode());

        // Canvas annotations.
        registry.Register(new NoteNode());
    }
}
