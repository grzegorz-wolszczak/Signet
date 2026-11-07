using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The steps that assert what the checker reported (upstream's <c>AssertionSteps</c>).</summary>
internal static class AssertionSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((w, _) =>
        {
            w.Report.GetAll(Severity.Fatal).Should().BeEmpty("there should be no fatal errors");
            w.Report.GetAll(Severity.Error).Should().BeEmpty("there should be no errors");
            w.Report.GetAll(Severity.Warning).Should().BeEmpty("there should be no warnings");
        }, "no( other) warning(s)/error(s) or error(s)/warning(s) are/is reported");

        steps.Add((w, _) => w.Report.GetAll(Severity.Usage).Should().BeEmpty("there should be no usages"),
            "no( other) usage(s) are/is reported");

        steps.Add((w, a) => AssertMessage(w, a.Get<Severity>(0), a.Get<MessageId>(1), a.Get<int>(2)),
            "(the ){severity} {messageId} is reported{messageQuantity}(  \\(){}");

        steps.Add((w, a) => AssertMessageList(w, a.Get<Severity>(0), a.Table!),
            "(the )following {severity}( ID)(s) are/is reported(  \\(){}");

        steps.Add((w, a) =>
        {
            w.LastAssertedMessage.Should().NotBeNull();
            w.LastAssertedMessage!.Message.Should().Be(a.Get<string>(0));
        }, "(the )message is {string}");

        steps.Add((w, a) =>
        {
            w.LastAssertedMessage.Should().NotBeNull();
            ContainsText(w.LastAssertedMessage!.Message, a.Get<string>(0));
        }, "(the )message contains {string}");

        steps.Add((w, _) =>
        {
            w.LastAssertedMessage.Should().NotBeNull();
            w.LastAssertedMessage!.Line.Should().NotBe(-1);
            w.LastAssertedMessage.Column.Should().NotBe(-1);
        }, "(the )message has line and column info");

        steps.Add((w, _) =>
        {
            foreach (MessageInfo message in w.Report.AllMessages.Where(m => m.Severity is Severity.Warning or Severity.Error))
            {
                message.Line.Should().NotBe(-1, message.ToString());
                message.Column.Should().NotBe(-1, message.ToString());
            }
        }, "all messages have line and column info");
    }

    private static void AssertMessage(ScenarioWorld world, Severity severity, MessageId id, int quantity)
    {
        if (quantity == 1)
        {
            MessageInfo? message = world.Report.Consume(id);
            message.Should().NotBeNull("a message with id {0} should have been reported", id.ToText());
            world.LastAssertedMessage = message;
            message!.Severity.Should().Be(severity);
            return;
        }

        List<MessageInfo> messages = world.Report.ConsumeAll(id);
        world.LastAssertedMessage = messages.LastOrDefault();
        messages.Should().HaveCount(quantity);
        messages.Should().OnlyContain(m => m.Severity == severity);
    }

    // The table lists, in order, every message of the severity: the id, and optionally a text the message contains.
    private static void AssertMessageList(ScenarioWorld world, Severity severity, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        List<MessageInfo> actual = world.Report.ConsumeAll(severity);
        actual.Should().HaveCount(rows.Count, "the messages of severity {0} should be the listed ones, but were: {1}", severity.ToText(), string.Join("; ", actual));
        for (int i = 0; i < rows.Count; i++)
        {
            MessageId expected = System.Enum.Parse<MessageId>(rows[i][0].Replace('-', '_'));
            actual[i].Id.Should().Be(expected);
            if (rows[i].Count > 1)
            {
                ContainsText(actual[i].Message, rows[i][1]);
            }
        }
    }

    // Java's String.contains: the empty string is in every string (AwesomeAssertions refuses to look for it).
    private static void ContainsText(string actual, string expected)
    {
        if (expected.Length != 0)
        {
            actual.Should().Contain(expected);
        }
    }
}
