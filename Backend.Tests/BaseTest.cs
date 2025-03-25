using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Backend.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PayPalCheckoutSdk.Orders;

namespace Backend.Tests
{
    public abstract class BaseTest : IClassFixture<WebApplicationFactory<Program>>
    {
        protected readonly WebApplicationFactory<Program> _factory;
        protected readonly IServiceProvider _serviceProvider;
        protected readonly AvanciraDbContext _dbContext;
        protected readonly HttpClient _client;

        protected BaseTest(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureServices(services =>
                {
                    ConfigureServices(services);
                });
            });


            // Build the service provider
            _serviceProvider = _factory.Services.CreateScope().ServiceProvider;

            // Resolve DbContext
            _dbContext = _serviceProvider.GetRequiredService<AvanciraDbContext>();

            // Initialize HttpClient
            _client = _factory.CreateClient();
            _client.BaseAddress = new Uri("http://localhost:5000");
        }

        /// <summary>
        /// A method that subclasses can override to register additional services.
        /// </summary>
        /// <param name="services">The service collection to register services with.</param>
        protected virtual void ConfigureServices(IServiceCollection services)
        {
            // This method is intentionally left empty, and can be overridden by subclasses.
        }

        /// <summary>
        /// Clear the in-memory database to prevent state leakage between tests.
        /// </summary>
        protected void ClearDatabase()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Database.EnsureCreated();
        }
    }
}

public static class OkResultUtility
{
    // Generic method to assert OkResult, serialize value, and return JObject
    public static JObject AssertOkResultAndParseJson(IActionResult result)
    {
        // Assert that the result is of type OkObjectResult
        if (result is OkObjectResult okResult)
        {
            // Serialize the OkResult value into JSON string
            var jsonResponse = JsonConvert.SerializeObject(okResult.Value);

            // Reparse the JSON to get JObject
            return JObject.Parse(jsonResponse);
        }

        // Throw an exception if the result is not an OkObjectResult
        throw new InvalidOperationException("Expected OkObjectResult but received a different result.");
    }
}
