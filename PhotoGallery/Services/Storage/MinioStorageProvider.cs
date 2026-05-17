using Amazon.S3;
using Amazon.S3.Model;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Minio S3-compatible storage provider for local development and testing
/// </summary>
public class MinioStorageProvider : IStorageProvider
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<MinioStorageProvider> _logger;

    /// <summary>
    /// Scheme used for pre-signed URLs. The AWS S3 SDK ignores the scheme of
    /// AmazonS3Config.ServiceURL when generating pre-signed URLs and defaults to
    /// HTTPS, so we must set <see cref="GetPreSignedUrlRequest.Protocol"/>
    /// explicitly to match the actual MinIO endpoint scheme. Driven by
    /// Storage:Minio:UseSSL — false in local dev (MinIO on http://localhost:9000),
    /// true in production behind TLS termination.
    /// </summary>
    private readonly Protocol _presignProtocol;

    public MinioStorageProvider(IAmazonS3 s3Client, IConfiguration configuration, ILogger<MinioStorageProvider> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
        _bucketName = configuration["Storage:Minio:BucketName"] ?? "photogallery";
        _presignProtocol = configuration.GetValue<bool>("Storage:Minio:UseSSL", false)
            ? Protocol.HTTPS
            : Protocol.HTTP;
    }

    public async Task<string> UploadAsync(string key, Stream fileStream, string contentType)
    {
        try
        {
            await EnsureBucketExistsAsync();
            
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request);
            
            _logger.LogInformation("File uploaded to Minio: {Key}", key);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Minio: {Key}", key);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(request);
            
            _logger.LogInformation("File downloaded from Minio: {Key}", key);
            return response.ResponseStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from Minio: {Key}", key);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            if (!await ExistsAsync(key))
                return false;

            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request);
            
            _logger.LogInformation("File deleted from Minio: {Key}", key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from Minio: {Key}", key);
            return false;
        }
    }

    public async Task<string?> GetUrlAsync(string key, int expirationMinutes = 60)
    {
        try
        {
            if (!await ExistsAsync(key))
                return null;

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                Verb = HttpVerb.GET,
                Protocol = _presignProtocol
            };

            var url = _s3Client.GetPreSignedURL(request);
            
            _logger.LogInformation("Presigned URL generated for Minio: {Key}", key);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating presigned URL for Minio: {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking object existence in Minio: {Key}", key);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListAsync(string prefix)
    {
        try
        {
            var items = new List<string>();
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(request);
                // S3Objects is NULL (not empty) when the prefix has no
                // matches — the AWS SDK reuses the wire null. Always-empty
                // fallback keeps the LINQ chain safe.
                var objects = response.S3Objects ?? new List<Amazon.S3.Model.S3Object>();
                items.AddRange(objects.Select(obj => obj.Key));
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated ?? false);

            _logger.LogInformation("Listed {Count} files from Minio with prefix: {Prefix}", items.Count, prefix);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files from Minio with prefix: {Prefix}", prefix);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string> GenerateWriteSasUrlAsync(string key, TimeSpan ttl)
    {
        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Expires = DateTime.UtcNow.Add(ttl),
                Verb = HttpVerb.PUT,
                Protocol = _presignProtocol
            };

            var url = _s3Client.GetPreSignedURL(request);
            _logger.LogInformation("Pre-signed PUT URL generated for Minio: {Key}", key);
            return await Task.FromResult(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating pre-signed PUT URL for Minio: {Key}", key);
            throw;
        }
    }

    public async Task<IEnumerable<string>> ListSubPrefixesAsync(string prefix)
    {
        try
        {
            var prefixes = new List<string>();
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix,
                Delimiter = "/"
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(request);
                if (response.CommonPrefixes != null)
                {
                    prefixes.AddRange(response.CommonPrefixes);
                }
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated ?? false);

            return prefixes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sub-prefixes from Minio with prefix: {Prefix}", prefix);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<BlobInfo>> ListWithMetadataAsync(string prefix)
    {
        try
        {
            var items = new List<BlobInfo>();
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(request);
                var objects = response.S3Objects ?? new List<Amazon.S3.Model.S3Object>();
                foreach (var obj in objects)
                {
                    items.Add(new BlobInfo(obj.Key, obj.Size ?? 0L, obj.LastModified ?? DateTimeOffset.UtcNow));
                }
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated ?? false);

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files with metadata from Minio with prefix: {Prefix}", prefix);
            return Enumerable.Empty<BlobInfo>();
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> keys)
    {
        var keyList = keys?.ToList() ?? new List<string>();
        if (keyList.Count == 0) return 0;

        int deleted = 0;
        // S3 DeleteObjects supports up to 1000 keys per request.
        const int BatchSize = 1000;
        foreach (var chunk in keyList.Chunk(BatchSize))
        {
            try
            {
                var req = new DeleteObjectsRequest
                {
                    BucketName = _bucketName,
                    Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList(),
                    Quiet = true,
                };
                var resp = await _s3Client.DeleteObjectsAsync(req);
                // S3 returns Quiet response: missing entries are silently treated as success.
                // Count chunk size minus reported errors as deleted.
                var errorCount = resp.DeleteErrors?.Count ?? 0;
                deleted += chunk.Length - errorCount;
                if (errorCount > 0)
                {
                    foreach (var err in resp.DeleteErrors!)
                    {
                        _logger.LogWarning("DeleteMany: failed to delete {Key}: {Code} {Message}", err.Key, err.Code, err.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeleteMany batch failed against Minio (chunk size {Count})", chunk.Length);
            }
        }
        return deleted;
    }
    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            var response = await _s3Client.ListBucketsAsync();
            
            if (!response.Buckets.Any(b => b.BucketName == _bucketName))
            {
                var request = new PutBucketRequest { BucketName = _bucketName };
                await _s3Client.PutBucketAsync(request);
                _logger.LogInformation("Created Minio bucket: {BucketName}", _bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring bucket exists in Minio: {BucketName}", _bucketName);
            throw;
        }
    }
}
