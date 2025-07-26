using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);

var awsConfig = builder.Configuration.GetSection("AWS");

var awsCredentials = new BasicAWSCredentials(
    awsConfig["AccessKey"],
    awsConfig["SecretKey"]);

var s3Client = new AmazonS3Client(
    awsCredentials,
    RegionEndpoint.GetBySystemName(awsConfig["Region"]));

builder.Services.AddSingleton<IAmazonS3>(s3Client);

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5500")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

string bucketName = "myvideouploadbucket2";

app.MapPost("/upload", async (IAmazonS3 s3, HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected form content.");

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    var title = form["title"].ToString();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    var key = $"videos/{Guid.NewGuid()}_{file.FileName}";

    using var stream = file.OpenReadStream();

    var putRequest = new PutObjectRequest
    {
        BucketName = bucketName,
        Key = key,
        InputStream = stream,
        ContentType = file.ContentType,
        Metadata =
        {
            ["x-amz-meta-title"] = title
        }
    };
    await s3.PutObjectAsync(putRequest);

    var presignedRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucketName,
        Key = key,
        Expires = DateTime.UtcNow.AddHours(1)
    };
    var url = s3.GetPreSignedURL(presignedRequest);

    return Results.Ok(new { VideoKey = key, VideoUrl = url, Title = title });
});

app.MapGet("/videos/{*key}", async (IAmazonS3 s3, string key) =>
{
    var metadata = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
    {
        BucketName = bucketName,
        Key = key
    });

    string title = metadata.Metadata.Keys.Contains("x-amz-meta-title")
    ? metadata.Metadata["x-amz-meta-title"]
    : "(Untitled)";
    
    var presignedRequest = new GetPreSignedUrlRequest
    {
        BucketName = bucketName,
        Key = key,
        Expires = DateTime.UtcNow.AddMinutes(15)
    };

    var url = s3.GetPreSignedURL(presignedRequest);

    return Results.Ok(new { VideoUrl = url, Title = title });
});

app.UseCors("FrontendPolicy");

app.Run();