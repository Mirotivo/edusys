using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests
{
    public class ConfigurationTests : BaseTest
    {
        public ConfigurationTests(WebApplicationFactory<Program> factory) : base(factory)
        { }

        [Fact]
        public void Should_Read_Configuration()
        {
            // Example: Reading a value from appsettings.json
            var configuration = _serviceProvider.GetRequiredService<IConfiguration>();

            var testValue = configuration["Avancira:App:Name"];
            Assert.NotNull(testValue);
            Assert.Equal("Avancira Pty Ltd", testValue);
        }
    }

}
