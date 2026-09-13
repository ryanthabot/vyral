using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.Controls.FlowCanvas;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Canvas wire-restore regression: when a pre-routed node collection is loaded
/// (DB workflow, draft restore, marketplace import), the canvas must draw the
/// node-to-node wires — not just the blocks.
/// </summary>
public class FlowCanvasWireTests
{
    private static void InvokeOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(20000), "STA work timed out");
        if (error != null)
        {
            throw error;
        }
    }

    private static void ApplyTemplate(FlowCanvasControl canvas)
    {
        canvas.Measure(new Size(1600, 900));
        canvas.Arrange(new Rect(0, 0, 1600, 900));
        canvas.ApplyTemplate();
    }

    private static System.Collections.IEnumerable GetWires(FlowCanvasControl canvas) =>
        (System.Collections.IEnumerable)typeof(FlowCanvasControl)
            .GetField("_wires", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(canvas)!;

    [Fact]
    public void Canvas_draws_wires_for_prerouted_nodes_on_load()
    {
        InvokeOnSta(() =>
        {
            var canvas = new FlowCanvasControl();
            ApplyTemplate(canvas);

            var first = new FlowCanvasNode
            {
                Id = "n1",
                BlockType = "send_message",
                Label = "One",
                X = 40,
                Y = 40
            };
            var second = new FlowCanvasNode
            {
                Id = "n2",
                BlockType = "send_dm",
                Label = "Two",
                X = 40,
                Y = 240
            };
            first.NextTargetId = "n2"; // wired BEFORE the canvas sees the nodes

            canvas.Nodes = new ObservableCollection<FlowCanvasNode> { first, second };

            var wires = GetWires(canvas);
            Assert.True(wires.Cast<object>().Any(), "expected at least 1 wire after load, found none");
        });
    }

    [Fact]
    public void Canvas_draws_wires_when_routing_is_set_after_load()
    {
        InvokeOnSta(() =>
        {
            var canvas = new FlowCanvasControl();
            ApplyTemplate(canvas);

            var first = new FlowCanvasNode { Id = "n1", BlockType = "send_message", X = 40, Y = 40 };
            var second = new FlowCanvasNode { Id = "n2", BlockType = "send_dm", X = 40, Y = 240 };
            canvas.Nodes = new ObservableCollection<FlowCanvasNode> { first, second };
            Assert.Empty(GetWires(canvas)); // nothing routed yet

            first.NextTargetId = "n2"; // user drags a wire (post-load change)

            Assert.True(GetWires(canvas).Cast<object>().Any(), "wire missing after routing change");
        });
    }
}
