using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Opf;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// The schemas that apply to a resource, by the context it is checked in (upstream's <c>ValidatorMap</c>): a schema is given
/// with a predicate, and a schema added more than once applies when any of its predicates holds. The schemas come in the order
/// they were first added.
/// </summary>
public sealed class ValidatorMap
{
    private readonly List<(XmlValidators Validator, Func<ValidationContext, bool> Applies)> _validators;

    private ValidatorMap(List<(XmlValidators, Func<ValidationContext, bool>)> validators)
    {
        _validators = validators;
    }

    /// <summary>A builder of a map.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>The schemas that apply to <paramref name="context"/>, compiled when first used (upstream builds them lazily as well).</summary>
    public IReadOnlyList<XmlValidator> GetValidators(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. _validators.Where(entry => entry.Applies(context)).Select(entry => entry.Validator.Get())];
    }

    /// <summary>Builds a <see cref="ValidatorMap"/>.</summary>
    public sealed class Builder
    {
        private readonly List<(XmlValidators Validator, Func<ValidationContext, bool> Applies)> _entries = [];

        /// <summary>Adds <paramref name="validators"/>, applicable when <paramref name="predicate"/> holds.</summary>
        public Builder PutAll(Func<ValidationContext, bool> predicate, params XmlValidators[] validators)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(validators);
            foreach (XmlValidators validator in validators)
            {
                _entries.Add((validator, predicate));
            }

            return this;
        }

        /// <summary>Adds <paramref name="validators"/>, applicable to any context.</summary>
        public Builder PutAll(params XmlValidators[] validators) => PutAll(static _ => true, validators);

        /// <summary>Adds <paramref name="validator"/>, applicable when <paramref name="predicate"/> holds.</summary>
        public Builder Put(Func<ValidationContext, bool> predicate, XmlValidators validator) => PutAll(predicate, validator);

        /// <summary>The map: a schema added more than once is applicable when any of its predicates holds.</summary>
        public ValidatorMap Build()
        {
            List<(XmlValidators, Func<ValidationContext, bool>)> combined = [];
            foreach (IGrouping<XmlValidators, (XmlValidators Validator, Func<ValidationContext, bool> Applies)> group in _entries.GroupBy(entry => entry.Validator))
            {
                Func<ValidationContext, bool>[] predicates = [.. group.Select(entry => entry.Applies)];
                combined.Add((group.Key, context => predicates.Any(predicate => predicate(context))));
            }

            return new ValidatorMap(combined);
        }
    }
}

/// <summary>The predicates on a <see cref="ValidationContext"/> that choose schemas (upstream's <c>ValidationContextPredicates</c>).</summary>
public static class ValidationContextPredicates
{
    /// <summary>The context has the property <paramref name="property"/>.</summary>
    public static Func<ValidationContext, bool> HasProp(Vocab.VocabProperty property) => context => context.HasProperty(property);

    /// <summary>The context has EPUBCheck's property <paramref name="property"/>.</summary>
    public static Func<ValidationContext, bool> HasProp(Vocab.EpubCheckVocab.Property property) => context => context.HasProperty(property);

    /// <summary>The publication has the <c>dc:type</c> <paramref name="type"/>.</summary>
    public static Func<ValidationContext, bool> HasPubType(PublicationType type) => context => context.PublicationTypes.Contains(type);

    /// <summary>The resource has the media type <paramref name="mimeType"/>.</summary>
    public static Func<ValidationContext, bool> MimeType(string mimeType) => context => context.MimeType == mimeType;

    /// <summary>The check has the profile <paramref name="profile"/>.</summary>
    public static Func<ValidationContext, bool> Profile(EpubProfile profile) => context => context.Profile == profile;

    /// <summary>The check is against EPUB <paramref name="version"/>.</summary>
    public static Func<ValidationContext, bool> Version(EpubVersion version) => context => context.Version == version;

    /// <summary>All of <paramref name="predicates"/> hold.</summary>
    public static Func<ValidationContext, bool> And(params Func<ValidationContext, bool>[] predicates) => context => predicates.All(predicate => predicate(context));

    /// <summary>Any of <paramref name="predicates"/> holds.</summary>
    public static Func<ValidationContext, bool> Or(params Func<ValidationContext, bool>[] predicates) => context => predicates.Any(predicate => predicate(context));

    /// <summary><paramref name="predicate"/> does not hold.</summary>
    public static Func<ValidationContext, bool> Not(Func<ValidationContext, bool> predicate) => context => !predicate(context);
}
