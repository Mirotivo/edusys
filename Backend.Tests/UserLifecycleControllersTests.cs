using System.Security.Claims;
using Backend.Controllers;
using Backend.Interfaces.Billing;
using Backend.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Stripe;

namespace Backend.Tests
{
    public class UserLifecycleControllersTests : BaseTest
    {
        private readonly UsersAPIController _userController;
        private readonly ListingsAPIController _listingController;
        private readonly PaymentsAPIController _paymentController;
        private readonly SubscriptionsAPIController _subscriptionController;
        private readonly ChatsAPIController _chatController;
        private readonly LessonsAPIController _lessonController;

        private Mock<IPaymentGateway>? _mockStripePaymentGateway;
        private Mock<IPaymentGatewayFactory>? _paymentGatewayFactoryMock;

        private IHttpContextAccessor? _httpContextAccessor;

        public UserLifecycleControllersTests(WebApplicationFactory<Program> factory) : base(factory)
        {
            var userService = _serviceProvider.GetRequiredService<IUserService>();
            var listingService = _serviceProvider.GetRequiredService<IListingService>();
            var paymentService = _serviceProvider.GetRequiredService<IPaymentService>();
            var subscriptionService = _serviceProvider.GetRequiredService<ISubscriptionService>();
            var chatService = _serviceProvider.GetRequiredService<IChatService>();
            var chatLogger = _serviceProvider.GetRequiredService<ILogger<ChatsAPIController>>();
            var lessonService = _serviceProvider.GetRequiredService<ILessonService>();
            var lessonLogger = _serviceProvider.GetRequiredService<ILogger<LessonsAPIController>>();
            var payPalAccountService = _serviceProvider.GetRequiredService<IPayPalAccountService>();
            var stripeAccountService = _serviceProvider.GetRequiredService<IStripeAccountService>();
            var stripeCardService = _serviceProvider.GetRequiredService<IStripeCardService>();

            _userController = new UsersAPIController(userService);
            _listingController = new ListingsAPIController(listingService);
            _paymentController = new PaymentsAPIController(userService, payPalAccountService, stripeAccountService, stripeCardService, paymentService);
            _subscriptionController = new SubscriptionsAPIController(subscriptionService);
            _chatController = new ChatsAPIController(chatService, listingService, chatLogger);
            _lessonController = new LessonsAPIController(lessonService, lessonLogger);

            var userManager = _serviceProvider.GetRequiredService<UserManager<User>>();
            RoleSeeder.Seed(_dbContext, userManager);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            // Unregister the existing implementation of IStripeCardService
            var serviceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStripeCardService));
            if (serviceDescriptor != null)
            {
                services.Remove(serviceDescriptor);
            }


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
            _mockStripePaymentGateway
                .Setup(gateway => gateway.CapturePaymentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync((string paymentId, string stripeCustomerId, decimal amount, string description) =>
                {
                    // Mock logic for testing
                    if (amount > 0)
                    {
                        return new PaymentResult
                        {
                            PaymentId = "charge_123",
                            ApprovalUrl = null, // Not needed for direct charge payments
                            Status = PaymentResultStatus.Completed
                        };
                    }
                    else
                    {
                        return new PaymentResult
                        {
                            PaymentId = null,
                            ApprovalUrl = null,
                            Status = PaymentResultStatus.Failed
                        };
                    }
                });


            // Create mock of PaymentGatewayFactory
            _paymentGatewayFactoryMock = new Mock<IPaymentGatewayFactory>();
            _paymentGatewayFactoryMock.Setup(f => f.GetPaymentGateway("Stripe")).Returns(_mockStripePaymentGateway.Object);
            services.AddSingleton(_paymentGatewayFactoryMock.Object);
        }

        private void SetControllerContext(ClaimsPrincipal? user = null, string? token = null)
        {
            var httpContext = new DefaultHttpContext
            {
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
                Request =
                    {
                        Headers =
                        {
                            ["Authorization"] = token != null ? $"Bearer {token}" : string.Empty
                        }
                    }
            };
            httpContext.RequestServices = _serviceProvider;

            _httpContextAccessor = _serviceProvider.GetRequiredService<IHttpContextAccessor>();

            var controllers = new ControllerBase[]
            {
                _userController, _listingController, _paymentController,
                _subscriptionController, _chatController, _lessonController
            };

            foreach (var controller in controllers)
            {
                _httpContextAccessor.HttpContext = httpContext;

                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                };
            }
        }

        [Fact]
        public async Task Test_UserLifecycle()
        {
            // Step 1: Register and login the tutor
            SetControllerContext();
            await RegisterUser(new RegisterViewModel
            {
                Email = "testtutor@example.com",
                Password = "Tutor@1234",
                ConfirmPassword = "Tutor@1234"
            });
            var tutorToken = await LoginUser(new LoginViewModel
            {
                Email = "testtutor@example.com",
                Password = "Tutor@1234"
            });
            Assert.False(string.IsNullOrEmpty(tutorToken), "Token not received.");

            // Step 2: Update Profile
            var tutorID = _dbContext.Users.FirstOrDefault(u => EF.Functions.Like(u.Email, "testtutor@example.com"))?.Id ?? string.Empty;
            var tutorIdentity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, tutorID.ToString()),
            });
            var tutorClaimsPrincipal = new ClaimsPrincipal(tutorIdentity);
            SetControllerContext(tutorClaimsPrincipal, tutorToken);
            await UpdateProfile();

            // Step 3: Create Listing
            SetControllerContext(tutorClaimsPrincipal, tutorToken);
            var listingId = await CreateListing();

            // Step 4: Register and login the student
            SetControllerContext();
            await RegisterUser(new RegisterViewModel
            {
                Email = "teststudent@example.com",
                Password = "Student@1234",
                ConfirmPassword = "Student@1234"
            });
            var studentToken = await LoginUser(new LoginViewModel
            {
                Email = "teststudent@example.com",
                Password = "Student@1234"
            });
            Assert.False(string.IsNullOrEmpty(studentToken), "Token not received.");

            // Step 5: Save Card
            var studentID = _dbContext.Users.FirstOrDefault(u => EF.Functions.Like(u.Email, "teststudent@example.com"))?.Id ?? string.Empty;
            var studentIdentity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, studentID.ToString()),
            });
            var studentClaimsPrincipal = new ClaimsPrincipal(studentIdentity);
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await SaveCard();

            // Step 6: Create Subscription
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await CreateSubscription();

            // Step 7: Search for Listings
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await SearchListings(listingId);

            // Step 8: Get Listing by ID
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await GetListingById(listingId);


            // Step 9: Send Message to Tutor
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await SendMessageToTutor(listingId);


            // Step 10: Propose a lesson
            SetControllerContext(studentClaimsPrincipal, studentToken);
            await ProposeLesson(listingId);

            // Step 11: Tutor retrieves lessons of the student
            SetControllerContext(tutorClaimsPrincipal, tutorToken);

            // Step 12: Tutor responds to the proposition
            // await TutorRespondToProposition(propostionId);

            // Step 13: Delete Account
            var deleteResponse = await _userController.DeleteAccount();
            var deleteOkResult = Assert.IsType<OkObjectResult>(deleteResponse);
            Assert.Equal(200, deleteOkResult.StatusCode);
        }

        private async Task RegisterUser(RegisterViewModel registerModel)
        {
            var registerResponse = await _userController.Register(registerModel);
            Assert.IsType<OkObjectResult>(registerResponse);
        }

        private async Task<string> LoginUser(LoginViewModel loginModel)
        {
            var loginResponse = await _userController.Login(loginModel);
            var loginOkResult = Assert.IsType<OkObjectResult>(loginResponse);
            var loginResult = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(loginOkResult.Value));
            var loginResultData = loginResult?.Data as JObject;

            return loginResultData?["token"]?.ToString();
        }

        private async Task UpdateProfile()
        {
            var updateResponse = await _userController.UpdateAsync(new UserDto
            {
                FirstName = "Test",
                LastName = "Tutor",
                PhoneNumber = "123-456-7890",
                Address = new AddressDto
                {
                    StreetAddress = "101 Grafton Street",
                    City = "Bondi Junction",
                    State = "NSW",
                    Country = "Australia",
                    PostalCode = "2022",
                    Latitude = -33.8912,
                    Longitude = 151.2646,
                    FormattedAddress = "101 Grafton Street, Bondi Junction NSW 2022, Australia"
                },
                SkypeId = "updated.skype@example.com"
            });
            var updateOkResult = Assert.IsType<OkObjectResult>(updateResponse);
            Assert.Equal(200, updateOkResult.StatusCode);
        }

        private async Task<int> CreateListing()
        {
            var testImageBytes = CreateTestImageAsByteArray(200, 200);
            var testImageStream = new MemoryStream(testImageBytes);
            var formFile = new FormFile(testImageStream, 0, testImageBytes.Length, "ListingImage", "testimage.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };

            var createListingDto = new CreateListingDto
            {
                ListingImage = formFile,
                Title = "Learn C# Programming",
                AboutLesson = "In this lesson, you will learn the fundamentals of C#.",
                AboutYou = "I am an experienced C# developer with a passion for teaching.",
                Locations = new List<string> { "Webcam", "TutorLocation" },
                LessonCategory = "Programming",
                Rates = new RatesDto { Hourly = 50, FiveHours = 250, TenHours = 500 }
            };
            var createListingResponse = await _listingController.Create(createListingDto);
            var createListingOkResult = Assert.IsType<CreatedAtActionResult>(createListingResponse);
            var createListingResult = createListingOkResult.Value as dynamic;
            return createListingResult?.Id ?? 0;
        }

        private async Task SaveCard()
        {
            var saveCardResponse = await _paymentController.SaveCard(new SaveCardDto
            {
                StripeToken = "tok_testStripeToken12345",
                Purpose = UserCardType.Paying
            });
            var saveCardOkResult = Assert.IsType<OkObjectResult>(saveCardResponse);
            var saveCardJson = JsonConvert.SerializeObject(saveCardOkResult.Value);
            var saveCardResult = JObject.Parse(saveCardJson);

            Assert.True(saveCardResult["Data"]?["success"]?.Value<bool>() ?? false, "Card save failed.");
            Assert.Equal("Card saved successfully.", saveCardResult["Data"]?["message"]?.ToString());
        }

        private async Task CreateSubscription()
        {
            var createSubscriptionResponse = await _subscriptionController.CreateSubscription(new SubscriptionRequestDto
            {
                Amount = 69,
                PaymentMethod = TransactionPaymentMethod.Stripe,
                PaymentType = TransactionPaymentType.StudentMembership,
                BillingFrequency = "Monthly"
            });
            var createSubscriptionOkResult = Assert.IsType<OkObjectResult>(createSubscriptionResponse);
            var createSubscriptionResult = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(createSubscriptionOkResult.Value));
            var createSubscriptionResultData = createSubscriptionResult?.Data as JObject;

            var subscriptionId = createSubscriptionResultData["SubscriptionId"]?.Value<int>() ?? 0;
            var transactionId = createSubscriptionResultData["TransactionId"]?.Value<int>() ?? 0;

            // Assert that the subscription and transaction IDs were returned successfully
            Assert.True(subscriptionId > 0, "Subscription ID is empty.");
            Assert.True(transactionId > 0, "Transaction ID is empty.");
        }

        private async Task SearchListings(int listingId)
        {
            var searchResponse = _listingController.Search("C#");
            var searchOkResult = Assert.IsType<OkObjectResult>(searchResponse);
            var searchResult = searchOkResult.Value as dynamic;
            Assert.NotEmpty(searchResult?.Data?.Results);
            //Assert.Equal(listingId, (int)searchResult?.Data?.Results?[0]?.Id);
        }

        private async Task GetListingById(int listingId)
        {
            var getListingResponse = _listingController.GetListingById(listingId);
            var getListingOkResult = Assert.IsType<OkObjectResult>(getListingResponse);
            dynamic listingDetails = getListingOkResult.Value as dynamic;
            Assert.Equal(listingId, (int)listingDetails?.Data?.Id);
        }

        private async Task SendMessageToTutor(int listingId)
        {
            var sendMessageDto = new SendMessageDto
            {
                ListingId = listingId,
                RecipientId = string.Empty,
                Content = "I need help with this listing."
            };
            var sendMessageResponse = _chatController.SendMessage(sendMessageDto);
            var sendMessageOkResult = Assert.IsType<OkObjectResult>(sendMessageResponse);
            dynamic sendMessageResult = sendMessageOkResult.Value as dynamic;
            Assert.NotNull(sendMessageResult);
            Assert.True((bool)sendMessageResult?.Success, "Message was not sent successfully.");
        }

        private async Task ProposeLesson(int listingId)
        {
            var lessonDto = new LessonDto
            {
                Topic = "Math Lesson",
                Date = DateTime.UtcNow.AddDays(1),
                Duration = TimeSpan.FromHours(1),
                Price = 30,
                ListingId = listingId,
            };
            var proposeLessonResponse = await _lessonController.ProposeLessonAsync(lessonDto);
            var parsedJson = OkResultUtility.AssertOkResultAndParseJson(proposeLessonResponse);
            Assert.NotNull(parsedJson);
            Assert.Equal("Lesson proposed successfully.", parsedJson["Data"]["Message"]?.ToString());
            Assert.Equal(listingId, parsedJson["Data"]["Lesson"]?["ListingId"].Value<int>());
        }

        private async Task<int> GetLessonByStudenttId(string studentId, int listingId)
        {
            var getLessonsResponse = await _lessonController.GetLessonsAsync(studentId, listingId);
            var getLessonsOkResult = Assert.IsType<OkObjectResult>(getLessonsResponse);
            var getLessonsResult = getLessonsOkResult.Value as dynamic;
            return getLessonsResult?.Id ?? 0;
        }

        private async Task TutorRespondToProposition(int propositionId)
        {
            var acceptProposition = true;
            var respondToPropositionResponse = await _lessonController.RespondToPropositionAsync(propositionId, acceptProposition);
            var respondToPropositionOkResult = Assert.IsType<OkObjectResult>(respondToPropositionResponse);
            dynamic respondResult = respondToPropositionOkResult.Value as dynamic;
            Assert.NotNull(respondResult);
            //Assert.Equal("Proposition accepted.", (string)respondResult?.Message);
        }

        // Helper function to create a test image
        public byte[] CreateTestImageAsByteArray(int width, int height)
        {
            // Create an empty image with a white background
            using (var bitmap = new SKBitmap(width, height))
            using (var canvas = new SKCanvas(bitmap))
            {
                // Clear background with color
                canvas.Clear(SKColors.AliceBlue);

                // Draw a rectangle
                var paint = new SKPaint
                {
                    Color = SKColors.Black,
                    StrokeWidth = 3,
                    Style = SKPaintStyle.Stroke
                };
                canvas.DrawRect(new SKRect(0, 0, width - 1, height - 1), paint);

                // Save the image to memory stream
                using (var ms = new MemoryStream())
                {
                    bitmap.Encode(ms, SKEncodedImageFormat.Jpeg, 100);
                    return ms.ToArray();
                }
            }
        }
    }
}
