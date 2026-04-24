using AccessPass.Models;
using System.Collections.Generic;

namespace AccessPass.Services;

/// <summary>
/// Service for resolving and processing access pass check-ins/check-outs.
/// </summary>
public interface IAccessPassService
{
    /// <summary>
    /// Attempts to resolve a scanned input (QR token or card ID) to an access pass
    /// and perform a check-in or check-out.
    /// Returns null if the input does not match any access pass (i.e. not an access pass scan).
    /// </summary>
    /// <param name="scannedInput">Raw scanned value (QR code content or card ID).</param>
    /// <param name="secret">Secret for QR token decryption.</param>
    AccessPassResult? TryResolve(string scannedInput, string secret);

    /// <summary>
    /// Gets all access passes.
    /// </summary>
    List<AccessPassEntry> GetAll();

    /// <summary>
    /// Creates a new access pass and returns it.
    /// </summary>
    AccessPassEntry Create(AccessPassEntry entry);

    /// <summary>
    /// Updates an existing access pass.
    /// </summary>
    bool Update(AccessPassEntry entry);

    /// <summary>
    /// Deactivates (soft-deletes) an access pass.
    /// </summary>
    bool Deactivate(string passId);

    /// <summary>
    /// Deletes an access pass and its avatar.
    /// </summary>
    bool Delete(string passId);

    /// <summary>
    /// Returns the full avatar file path for a given pass, or null.
    /// </summary>
    string? GetAvatarPath(string passId);

    /// <summary>
    /// Saves an avatar image for a pass. Returns the file name.
    /// </summary>
    string SaveAvatar(string passId, byte[] imageBytes, string extension = ".jpg");
}
