using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Interfaces.Billing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Moq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Stripe;
using Xunit;

namespace Backend.Tests.Services
{
    public class PaymentServiceTests : BaseTest
    {
        private IStripeCardService _stripeCardService;
        private IPaymentService _paymentService;

        private Mock<IStripeCardService>? _mockStripeCardService;
        private Mock<IPaymentGateway>? _mockStripePaymentGateway;
        private Mock<IPaymentGatewayFactory>? _paymentGatewayFactoryMock;

        public PaymentServiceTests(WebApplicationFactory<Program> factory) : base(factory)
        {
            // Initialize the service
            _stripeCardService = _serviceProvider.GetRequiredService<IStripeCardService>();
            _paymentService = _serviceProvider.GetRequiredService<IPaymentService>();
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            // Unregister the existing implementation of IStripeCardService
            var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStripeCardService));
            if (serviceDescriptor != null)
            {
                services.Remove(serviceDescriptor);
            }


            //// Create mock of StripeCardService
            //_mockStripeCardService = new Mock<IStripeCardService>();
            //_mockStripeCardService
            //    .Setup(service => service.CreateAsync(It.IsAny<string>(), It.IsAny<CardCreateOptions>()))
            //    .ReturnsAsync(new Card
            //    {
            //        Id = "card_123",
            //        Last4 = "4242",
            //        ExpMonth = 12,
            //        ExpYear = 2030
            //    });
            //services.AddSingleton(_mockStripeCardService.Object);


            // Create mock of StripePaymentGateway
            _mockStripePaymentGateway = new Mock<IPaymentGateway>();
            _mockStripePaymentGateway
                .Setup(gateway => gateway.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((decimal amount, string currency, string returnUrl, string cancelUrl) => new PaymentResult
                {
                    PaymentId = "payment_123",
                    ApprovalUrl = "https://example.com/approval",
                    Status = PaymentResultStatus.Completed
                });
            //_mockStripePaymentGateway
            //    .Setup(gateway => gateway.CapturePaymentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            //    .ReturnsAsync((string paymentId, string stripeCustomerId, decimal amount, string description) =>
            //    {
            //        // Mock logic for testing
            //        if (amount > 0)
            //        {
            //            return new PaymentResult
            //            {
            //                PaymentId = "charge_123",
            //                ApprovalUrl = null, // Not needed for direct charge payments
            //                Status = PaymentResultStatus.Completed
            //            };
            //        }
            //        else
            //        {
            //            return new PaymentResult
            //            {
            //                PaymentId = null,
            //                ApprovalUrl = null,
            //                Status = PaymentResultStatus.Failed
            //            };
            //        }
            //    });


            // Create mock of PaymentGatewayFactory
            _paymentGatewayFactoryMock = new Mock<IPaymentGatewayFactory>();
            _paymentGatewayFactoryMock.Setup(f => f.GetPaymentGateway("Stripe")).Returns(_mockStripePaymentGateway.Object);
            services.AddSingleton(_paymentGatewayFactoryMock.Object);
        }

        [Fact]
        public async Task CreatePaymentAsync_ShouldReturnPaymentResult_WhenValidRequestIsProvided()
        {
            // Arrange
            var request = new PaymentRequestDto
            {
                Amount = 100,
                Currency = "USD",
                ReturnUrl = "https://return.url",
                CancelUrl = "https://cancel.url",
                Gateway = "Stripe"
            };


            // Act
            var result = await _paymentService.CreatePaymentAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(PaymentResultStatus.Completed, result.Status);
        }

        public string GenerateToken(string url)
        {
            // Configure ChromeDriver options (headless mode optional)
            ChromeOptions options = new ChromeOptions();
            //options.AddArgument("--headless"); // Remove this line if you want to see the browser UI

            using (IWebDriver driver = new ChromeDriver(options))
            {
                try
                {
                    // Navigate to the page hosting the form
                    driver.Navigate().GoToUrl("http://127.0.0.1:5500/test.html");

                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                    // Fill the card number
                    var cardNumberFrame = wait.Until(drv => drv.FindElement(By.CssSelector("iframe[title='Secure card number input frame']")));
                    driver.SwitchTo().Frame(cardNumberFrame);
                    var cardNumberInput = wait.Until(drv => drv.FindElement(By.CssSelector("input[placeholder='1234 1234 1234 1234']")));
                    cardNumberInput.Click(); // Click to focus
                    cardNumberInput.SendKeys("4242424242424242");
                    driver.SwitchTo().DefaultContent(); // Switch back to main document

                    // Fill the card expiry
                    var cardExpiryFrame = wait.Until(drv => drv.FindElement(By.CssSelector("iframe[title='Secure expiration date input frame']")));
                    driver.SwitchTo().Frame(cardExpiryFrame);
                    var cardExpiryInput = wait.Until(drv => drv.FindElement(By.CssSelector("input[placeholder='MM / YY']")));
                    cardExpiryInput.Click(); // Click to focus
                    cardExpiryInput.SendKeys("12/34");
                    driver.SwitchTo().DefaultContent(); // Switch back to main document

                    // Fill the card CVC
                    var cardCvcFrame = wait.Until(drv => drv.FindElement(By.CssSelector("iframe[title='Secure CVC input frame']")));
                    driver.SwitchTo().Frame(cardCvcFrame);
                    var cardCvcInput = wait.Until(drv => drv.FindElement(By.CssSelector("input[placeholder='CVC']")));
                    cardCvcInput.Click(); // Click to focus
                    cardCvcInput.SendKeys("123");
                    driver.SwitchTo().DefaultContent(); // Switch back to main document

                    // Submit the form (optional)
                    var submitButton = wait.Until(drv => drv.FindElement(By.Id("submit")));
                    submitButton.Click();

                    // Wait for the token to be populated in the resultField
                    var resultField = wait.Until(drv =>
                    {
                        var element = drv.FindElement(By.Id("resultField"));
                        return !string.IsNullOrEmpty(element.GetAttribute("value")) ? element : null;
                    });

                    // Extract the token value
                    var token = resultField.GetAttribute("value");
                    Console.WriteLine("Generated Token: " + token);

                    return token;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    return null; // Return null in case of an error
                }
            }
        }

        //[Fact]
        //public async Task SaveCardAsync_ShouldSaveCard_WhenValidRequestIsProvided()
        //{
        //    // Arrange
        //    var user = new User
        //    {
        //        Id = 1,
        //        Email = "user@example.com",
        //        FirstName = "John",
        //        LastName = "Doe"
        //    };
        //    await _dbContext.Users.AddAsync(user);
        //    await _dbContext.SaveChangesAsync();

        //    string url = "http://127.0.0.1:5500/test.html";

        //    // Call the GenerateToken function
        //    string token = GenerateToken(url);

        //    var request = new SaveCardDto
        //    {
        //        StripeToken = token,
        //        Purpose = CardType.Receiving
        //    };

        //    // Act
        //    var result = await _paymentService.SaveCardAsync(user.Id, request);

        //    // Assert
        //    Assert.True(result);
        //    var savedCard = await _dbContext.UserCards.FirstOrDefaultAsync(c => c.UserId == user.Id);
        //    Assert.NotNull(savedCard);
        //    Assert.Equal("4242", savedCard.Last4);
        //}

        [Fact]
        public async Task GetSavedCardsAsync_ShouldReturnListOfCards_WhenCardsExist()
        {
            // Arrange
            var userId = "1";
            await _dbContext.UserCards.AddRangeAsync(
                new UserCard { UserId = userId, Last4 = "1111", ExpMonth = 12, ExpYear = 2025, Brand = "Visa", Type = UserCardType.Receiving },
                new UserCard { UserId = userId, Last4 = "2222", ExpMonth = 11, ExpYear = 2024, Brand = "Visa", Type = UserCardType.Paying }
            );
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _stripeCardService.GetUserCardsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
            Assert.Contains(result, card => card.Last4 == "1111");
            Assert.Contains(result, card => card.Last4 == "2222");
        }

        [Fact]
        public async Task GetPaymentHistoryAsync_ShouldReturnPaymentHistory_WhenUsersPayEachOther()
        {
            // Arrange
            var senderId = "1";
            var recipientId = "2";

            // Add users
            var sender = new User { Id = senderId, Email = "sender@example.com" };
            var recipient = new User { Id = recipientId, Email = "recipient@example.com" };
            await _dbContext.Users.AddRangeAsync(sender, recipient);

            // Add wallets
            var senderWallet = new Wallet { UserId = senderId, Balance = 50 };
            var recipientWallet = new Wallet { UserId = recipientId, Balance = 150 };
            await _dbContext.Wallets.AddRangeAsync(senderWallet, recipientWallet);

            // Add transactions
            await _dbContext.Transactions.AddRangeAsync(
                new Transaction
                {
                    SenderId = senderId,
                    RecipientId = recipientId,
                    Amount = 50,
                    PlatformFee = 5,
                    Status = TransactionStatus.Completed,
                    TransactionDate = DateTime.UtcNow.AddDays(-1)
                },
                new Transaction
                {
                    SenderId = recipientId,
                    RecipientId = senderId,
                    Amount = 100,
                    PlatformFee = 10,
                    Status = TransactionStatus.Completed,
                    TransactionDate = DateTime.UtcNow
                }
            );
            await _dbContext.SaveChangesAsync();

            // Act
            var senderResult = await _paymentService.GetPaymentHistoryAsync(senderId);
            var recipientResult = await _paymentService.GetPaymentHistoryAsync(recipientId);

            // Assert for sender
            Assert.NotNull(senderResult);
            Assert.Equal(senderWallet.Balance, senderResult.WalletBalance);
            Assert.Equal(2, senderResult.Transactions.Count);

            // Assert for recipient
            Assert.NotNull(recipientResult);
            Assert.Equal(recipientWallet.Balance, recipientResult.WalletBalance);
            Assert.Equal(2, recipientResult.Transactions.Count);
        }
    }
}
