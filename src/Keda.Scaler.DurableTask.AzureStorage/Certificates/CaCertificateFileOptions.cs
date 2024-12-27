// Copyright © William Sugarman.
// Licensed under the MIT License.

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;

namespace Keda.Scaler.DurableTask.AzureStorage.Certificates;

internal class CaCertificateFileOptions
{
    [Required]
    public string Path { get; set; } = default!;

    [Range(0, 1000 * 60)]
    public int ReloadDelayMs { get; set; } = 250;

    public X509Certificate2 Load()
        => X509Certificate2.CreateFromPemFile(Path);
}
