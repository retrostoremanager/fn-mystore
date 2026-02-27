using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Helpers;
using MyStore.Functions.Services;
using MyStore.Models;
using MyStore.Repositories;

namespace MyStore.Functions;

/// <summary>
/// API for company/store profile (EPIC-0-007).
/// </summary>
public class CompanyProfileFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICompanyRepository _companyRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly LogoStorageService _logoStorage;
    private readonly ILogger _logger;

    public CompanyProfileFunctions(
        ICompanyRepository companyRepository,
        ILocationRepository locationRepository,
        LogoStorageService logoStorage,
        ILoggerFactory loggerFactory)
    {
        _companyRepository = companyRepository;
        _locationRepository = locationRepository;
        _logoStorage = logoStorage;
        _logger = loggerFactory.CreateLogger<CompanyProfileFunctions>();
    }

    [Function("GetCompanyProfile")]
    public async Task<HttpResponseData> GetProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "company/profile")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var profile = await _companyRepository.GetProfileAsync(companyId);
            if (profile == null)
            {
                var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("Company not found.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            var locations = (await _locationRepository.GetByCompanyIdAsync(companyId)).ToList();
            var response = ApiResponse<CompanyProfileResponse>.SuccessResponse(new CompanyProfileResponse
            {
                Profile = profile,
                Locations = locations
            });
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized profile retrieval");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving company profile");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("An error occurred while retrieving the profile.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("UpdateCompanyProfile")]
    public async Task<HttpResponseData> UpdateProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "company/profile")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<CompanyProfileUpdateRequest>(body, JsonOptions);
            if (request == null)
            {
                var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("Invalid request body.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            await _companyRepository.UpdateProfileAsync(companyId, request);

            var profile = await _companyRepository.GetProfileAsync(companyId);
            var response = ApiResponse<CompanyProfile>.SuccessResponse(profile!, "Profile updated successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized profile update");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating company profile");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("An error occurred while updating the profile.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("UploadCompanyLogo")]
    public async Task<HttpResponseData> UploadLogo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "company/profile/logo")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<LogoUploadRequest>(body, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.File) || string.IsNullOrWhiteSpace(request.FileName))
            {
                var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("File and FileName are required.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(request.File);
            }
            catch (FormatException)
            {
                var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("Invalid base64 file data.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            string logoUrl;
            try
            {
                logoUrl = await _logoStorage.UploadAsync(companyId, fileBytes, request.FileName, request.ContentType ?? "image/png");
            }
            catch (ArgumentException ex)
            {
                var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse(ex.Message);
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            await _companyRepository.UpdateProfileAsync(companyId, new CompanyProfileUpdateRequest { LogoUrl = logoUrl });

            var profile = await _companyRepository.GetProfileAsync(companyId);
            var response = ApiResponse<CompanyProfile>.SuccessResponse(profile!, "Logo uploaded successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized logo upload");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading company logo");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("An error occurred while uploading the logo.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("DeleteCompanyLogo")]
    public async Task<HttpResponseData> DeleteLogo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "company/profile/logo")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            await _logoStorage.DeleteAsync(companyId);
            await _companyRepository.UpdateProfileAsync(companyId, new CompanyProfileUpdateRequest { LogoUrl = string.Empty });

            var profile = await _companyRepository.GetProfileAsync(companyId);
            var response = ApiResponse<CompanyProfile>.SuccessResponse(profile!, "Logo removed successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized logo delete");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting company logo");
            var errorResponse = ApiResponse<CompanyProfile>.ErrorResponse("An error occurred while removing the logo.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetLocations")]
    public async Task<HttpResponseData> GetLocations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "company/locations")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);
            var locations = (await _locationRepository.GetByCompanyIdAsync(companyId)).ToList();
            var response = ApiResponse<IEnumerable<Location>>.SuccessResponse(locations);
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized locations retrieval");
            var errorResponse = ApiResponse<IEnumerable<Location>>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations");
            var errorResponse = ApiResponse<IEnumerable<Location>>.ErrorResponse("An error occurred while retrieving locations.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("CreateLocation")]
    public async Task<HttpResponseData> CreateLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "company/locations")] HttpRequestData req)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<LocationCreateRequest>(body, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                var errorResponse = ApiResponse<Location>.ErrorResponse("Location name is required.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            var location = new Location
            {
                CompanyId = companyId,
                Name = request.Name.Trim(),
                Address = request.Address?.Trim(),
                City = request.City?.Trim(),
                State = request.State?.Trim(),
                ZipCode = request.ZipCode?.Trim(),
                Phone = request.Phone?.Trim(),
                Timezone = request.Timezone?.Trim(),
                IsPrimary = request.IsPrimary
            };

            var id = await _locationRepository.CreateAsync(location);
            location.Id = id;
            location.CreatedDate = DateTime.UtcNow;

            var response = ApiResponse<Location>.SuccessResponse(location, "Location created successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.Created);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized location create");
            var errorResponse = ApiResponse<Location>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location");
            var errorResponse = ApiResponse<Location>.ErrorResponse("An error occurred while creating the location.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("UpdateLocation")]
    public async Task<HttpResponseData> UpdateLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "company/locations/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var existing = await _locationRepository.GetByIdAsync(id, companyId);
            if (existing == null)
            {
                var errorResponse = ApiResponse<Location>.ErrorResponse("Location not found.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            string body;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var request = JsonSerializer.Deserialize<LocationUpdateRequest>(body, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                var errorResponse = ApiResponse<Location>.ErrorResponse("Location name is required.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.BadRequest);
            }

            existing.Name = request.Name.Trim();
            existing.Address = request.Address?.Trim();
            existing.City = request.City?.Trim();
            existing.State = request.State?.Trim();
            existing.ZipCode = request.ZipCode?.Trim();
            existing.Phone = request.Phone?.Trim();
            existing.Timezone = request.Timezone?.Trim();
            existing.IsPrimary = request.IsPrimary;

            await _locationRepository.UpdateAsync(existing);

            var response = ApiResponse<Location>.SuccessResponse(existing, "Location updated successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized location update");
            var errorResponse = ApiResponse<Location>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location");
            var errorResponse = ApiResponse<Location>.ErrorResponse("An error occurred while updating the location.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    [Function("DeleteLocation")]
    public async Task<HttpResponseData> DeleteLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "company/locations/{id}")] HttpRequestData req,
        int id)
    {
        try
        {
            var companyId = CompanyHelper.GetCompanyIdRequired(req);

            var deleted = await _locationRepository.DeleteAsync(id, companyId);
            if (!deleted)
            {
                var errorResponse = ApiResponse<object>.ErrorResponse("Location not found.");
                return await CreateHttpResponse(req, errorResponse, HttpStatusCode.NotFound);
            }

            var response = ApiResponse<object>.SuccessResponse(new { }, "Location deleted successfully.");
            return await CreateHttpResponse(req, response, HttpStatusCode.OK);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized location delete");
            var errorResponse = ApiResponse<object>.ErrorResponse(ex.Message);
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location");
            var errorResponse = ApiResponse<object>.ErrorResponse("An error occurred while deleting the location.");
            return await CreateHttpResponse(req, errorResponse, HttpStatusCode.InternalServerError);
        }
    }

    private static async Task<HttpResponseData> CreateHttpResponse<T>(HttpRequestData req, ApiResponse<T> apiResponse, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return response;
    }
}
