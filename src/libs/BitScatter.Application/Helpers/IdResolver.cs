using System.Text.RegularExpressions;
using BitScatter.Application.Interfaces;

namespace BitScatter.Application.Helpers;

/// <summary>
/// Provides utility methods to resolve file manifest identifiers from user inputs.
/// </summary>
public static class IdResolver
{
    /// <summary>
    /// Resolves a file manifest ID from a given string identifier, which can be either a full GUID
    /// or a unique prefix of a GUID.
    /// </summary>
    /// <param name="repository">The file manifest repository to query.</param>
    /// <param name="input">The user input string representing the ID or ID prefix.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved <see cref="Guid"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when input is empty or null.</exception>
    /// <exception cref="FormatException">Thrown when input format is not a valid GUID or hex prefix.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no matching manifests are found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when prefix is ambiguous and matches multiple manifests.</exception>
    public static async Task<Guid> ResolveIdAsync(IFileManifestRepository repository, string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("File identifier cannot be empty.", nameof(input));
        }

        // 1. If it's a full valid GUID, return it directly.
        if (Guid.TryParse(input, out var exactGuid))
        {
            return exactGuid;
        }

        // Normalize prefix: remove hyphens and convert to lower-case.
        var normalizedInput = input.Replace("-", "").ToLowerInvariant();

        // 2. Validate prefix format (only hex characters, length at least 4)
        if (normalizedInput.Length < 4 || !Regex.IsMatch(normalizedInput, "^[0-9a-f]+$"))
        {
            throw new FormatException($"Invalid file identifier '{input}'. Must be a valid GUID or a hexadecimal prefix of at least 4 characters.");
        }

        // Get all completed manifests
        var manifests = await repository.GetAllAsync(cancellationToken);

        // Find matches by GUID prefix (ignoring hyphens and casing)
        var matches = manifests
            .Where(m => m.Id.ToString().Replace("-", "").StartsWith(normalizedInput))
            .ToList();

        if (matches.Count == 0)
        {
            throw new KeyNotFoundException($"No files found matching ID prefix '{input}'.");
        }

        if (matches.Count > 1)
        {
            var matchIds = string.Join(", ", matches.Select(m => m.Id.ToString()));
            throw new InvalidOperationException($"Ambiguous ID prefix '{input}'. Multiple matches found: {matchIds}");
        }

        return matches[0].Id;
    }
}
