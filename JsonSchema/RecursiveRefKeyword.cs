﻿using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Pointer;

namespace Json.Schema
{
	/// <summary>
	/// Handles `$recursiveRef`.
	/// </summary>
	[SchemaKeyword(Name)]
	[SchemaDraft(Draft.Draft201909)]
	[Vocabulary(Vocabularies.Core201909Id)]
	[JsonConverter(typeof(RecursiveRefKeywordJsonConverter))]
	public class RecursiveRefKeyword : IJsonSchemaKeyword, IEquatable<RecursiveRefKeyword>
	{
		internal const string Name = "$recursiveRef";

		/// <summary>
		/// The URI reference.
		/// </summary>
		public Uri Reference { get; }

		/// <summary>
		/// Creates a new <see cref="RecursiveRefKeyword"/>.
		/// </summary>
		/// <param name="value">The URI.</param>
		public RecursiveRefKeyword(Uri value)
		{
			Reference = value ?? throw new ArgumentNullException(nameof(value));
		}

		/// <summary>
		/// Provides validation for the keyword.
		/// </summary>
		/// <param name="context">Contextual details for the validation process.</param>
		public void Validate(ValidationContext context)
		{
			context.EnterKeyword(Name);
			var parts = Reference.OriginalString.Split(new[] {'#'}, StringSplitOptions.None);
			var baseUri = parts[0];
			var fragment = parts.Length > 1 ? parts[1] : null;

			Uri? newUri;
			JsonSchema? baseSchema = null;
			if (!string.IsNullOrEmpty(baseUri))
			{
				if (Uri.TryCreate(baseUri, UriKind.Absolute, out newUri))
					baseSchema = context.Options.SchemaRegistry.Get(newUri);
				else if (context.CurrentUri != null)
				{
					var uriFolder = context.CurrentUri.OriginalString.EndsWith("/")
						? context.CurrentUri
						: context.CurrentUri.GetParentUri();
					newUri = uriFolder;
					var newBaseUri = new Uri(uriFolder, baseUri);
					if (!string.IsNullOrEmpty(fragment))
						newUri = newBaseUri;
					baseSchema = context.Options.SchemaRegistry.Get(newBaseUri);
				}
			}
			else
			{
				newUri = context.CurrentUri;
				baseSchema = context.CurrentAnchor ?? context.Options.SchemaRegistry.Get(newUri) ?? context.SchemaRoot;
			}

			var refFragment = string.IsNullOrEmpty(fragment) ? $"__{nameof(RecursiveRefKeyword)}__" : fragment;
			var absoluteReference = SchemaRegistry.GetFullReference(newUri, refFragment);
			if (context.NavigatedReferences.Contains(absoluteReference))
			{
				context.IsValid = false;
				context.Message = "Encountered recursive reference";
				context.ExitKeyword(Name, context.IsValid);
				return;
			}

			context.NavigatedReferences.Add(absoluteReference);

			JsonSchema? schema;
			if (!string.IsNullOrEmpty(fragment) && AnchorKeyword.AnchorPattern.IsMatch(fragment!))
				schema = context.Options.SchemaRegistry.Get(newUri, fragment);
			else
			{
				if (baseSchema == null)
				{
					context.IsValid = false;
					context.Message = $"Could not resolve base URI `{baseUri}`";
					context.ExitKeyword(Name, context.IsValid);
					return;
				}

				if (!string.IsNullOrEmpty(fragment))
				{
					fragment = $"#{fragment}";
					if (!JsonPointer.TryParse(fragment, out var pointer))
					{
						context.IsValid = false;
						context.Message = $"Could not parse pointer `{fragment}`";
						context.ExitKeyword(Name, context.IsValid);
						return;
					}

					(schema, newUri) = baseSchema.FindSubschema(pointer, newUri);
				}
				else
					schema = baseSchema;
			}

			if (schema == null)
			{
				context.IsValid = false;
				context.Message = $"Could not resolve RecursiveReference `{Reference}`";
				context.ExitKeyword(Name, context.IsValid);
				return;
			}

			var subContext = ValidationContext.From(context, newUri: newUri);
			if (!ReferenceEquals(baseSchema, context.SchemaRoot))
				subContext.SchemaRoot = baseSchema!;
			schema.ValidateSubschema(subContext);
			context.NestedContexts.Add(subContext);
			context.ConsolidateAnnotations();
			context.IsValid = subContext.IsValid;
			context.ExitKeyword(Name, context.IsValid);
		}

		/// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
		/// <param name="other">An object to compare with this object.</param>
		/// <returns>true if the current object is equal to the <paramref name="other">other</paramref> parameter; otherwise, false.</returns>
		public bool Equals(RecursiveRefKeyword? other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Equals(Reference, other.Reference);
		}

		/// <summary>Determines whether the specified object is equal to the current object.</summary>
		/// <param name="obj">The object to compare with the current object.</param>
		/// <returns>true if the specified object  is equal to the current object; otherwise, false.</returns>
		public override bool Equals(object obj)
		{
			return Equals(obj as RecursiveRefKeyword);
		}

		/// <summary>Serves as the default hash function.</summary>
		/// <returns>A hash code for the current object.</returns>
		public override int GetHashCode()
		{
			return Reference.GetHashCode();
		}
	}

	internal class RecursiveRefKeywordJsonConverter : JsonConverter<RecursiveRefKeyword>
	{
		public override RecursiveRefKeyword Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var uri = reader.GetString(); 
			return new RecursiveRefKeyword(new Uri(uri, UriKind.RelativeOrAbsolute));


		}
		public override void Write(Utf8JsonWriter writer, RecursiveRefKeyword value, JsonSerializerOptions options)
		{
			writer.WritePropertyName(RecursiveRefKeyword.Name);
			JsonSerializer.Serialize(writer, value.Reference, options);
		}
	}
}
