// Copyright © William Sugarman.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Threading;
using Keda.Scaler.DurableTask.AzureStorage.Certificates;
using Microsoft.Extensions.Options;
using Xunit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Certificate;

namespace Keda.Scaler.DurableTask.AzureStorage.Test.Certificates;

[Collection(nameof(CertificateTestCollection))]
public class ConfigureCustomTrustStoreTest(TestDirectory directory) : IClassFixture<TestDirectory>
{
    private readonly TestDirectory _directory = directory;

    [Fact]
    public void GivenNullOptions_WhenCreatingConfigure_ThenThrowArgumentNullException()
    {
        using ReaderWriterLockSlim readerWriterLock = new();
        _ = Assert.Throws<ArgumentNullException>(() => new ConfigureCustomTrustStore(null!, readerWriterLock));

        IOptions<ClientCertificateValidationOptions> options = Options.Create<ClientCertificateValidationOptions>(null!);
        _ = Assert.Throws<ArgumentNullException>(() => new ConfigureCustomTrustStore(options, readerWriterLock));

        options = Options.Create(new ClientCertificateValidationOptions { CertificateAuthority = null });
        _ = Assert.Throws<ArgumentNullException>(() => new ConfigureCustomTrustStore(options, readerWriterLock));
    }

    [Fact]
    public void GivenNullReaderWriterLock_WhenCreatingConfigure_ThenThrowArgumentNullException()
    {
        IOptions<ClientCertificateValidationOptions> options = Options.Create(new ClientCertificateValidationOptions { CertificateAuthority = new CaCertificateFileOptions() });
        _ = Assert.Throws<ArgumentNullException>(() => new ConfigureCustomTrustStore(options, null!));
    }

    [Fact]
    public async Task GivenCertificate_WhenConfiguringOptions_ThenSetCustomTrustStore()
    {
        // Create the certificate and write to disk
        const string CertName = "example.crt";
        string certPath = Path.Combine(_directory.Path, CertName);

        using RSA key = RSA.Create();
        using X509Certificate2 expected = key.CreateSelfSignedCertificate();
        await File.WriteAllBytesAsync(certPath, expected.Export(X509ContentType.Pkcs12));

        // Configure the options
        using ReaderWriterLockSlim readerWriterLock = new();
        ClientCertificateValidationOptions validationOptions = new()
        {
            CertificateAuthority = new CaCertificateFileOptions { Path = certPath },
        };

        CertificateAuthenticationOptions options = new();
        using ConfigureCustomTrustStore configure = new(Options.Create(validationOptions), readerWriterLock);
        configure.Configure(options);

        X509Certificate2 actual = Assert.Single(options.CustomTrustStore);
        Assert.Equal(expected.Thumbprint, actual.Thumbprint);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, options.ChainTrustValidationMode);
    }

    [Fact(Timeout = 1000 * 10)]
    public async Task GivenCertificateFileChange_WhenConfiguringOptions_ThenUpdateCustomTrustStore()
    {
        // Create the certificate and write to disk
        const string CertName = "example.crt";
        string certPath = Path.Combine(_directory.Path, CertName);

        using RSA key1 = RSA.Create();
        using X509Certificate2 expected1 = key1.CreateSelfSignedCertificate();
        await File.WriteAllBytesAsync(certPath, expected1.Export(X509ContentType.Pkcs12));

        // Configure the options
        using ReaderWriterLockSlim readerWriterLock = new();
        ClientCertificateValidationOptions validationOptions = new()
        {
            CertificateAuthority = new CaCertificateFileOptions { Path = certPath },
        };

        CertificateAuthenticationOptions options = new();
        using ConfigureCustomTrustStore configure = new(Options.Create(validationOptions), readerWriterLock);
        configure.Configure(options);

        X509Certificate2 actual = Assert.Single(options.CustomTrustStore);
        Assert.Equal(expected1.Thumbprint, actual.Thumbprint);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, options.ChainTrustValidationMode);

        // Update the certificate file after a half second
        await Task.Delay(500);

        using RSA key2 = RSA.Create();
        using X509Certificate2 expected2 = key2.CreateSelfSignedCertificate();
        await File.WriteAllBytesAsync(certPath, expected2.Export(X509ContentType.Pkcs12));

        // Check for the updated certificate
        do
        {
            configure.Configure(options);
            actual = Assert.Single(options.CustomTrustStore);
        } while (actual.Thumbprint != expected2.Thumbprint);

        Assert.Equal(X509ChainTrustMode.CustomRootTrust, options.ChainTrustValidationMode);
    }
}
