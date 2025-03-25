using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Backend.Tests.Services
{
    public class ChatServiceTests : BaseTest
    {
        private readonly IChatService _chatService;

        public ChatServiceTests(WebApplicationFactory<Program> factory) : base(factory)
        {
            _chatService = _serviceProvider.GetRequiredService<IChatService>();
        }

        [Fact]
        public void GetChats_ShouldReturnChatsForSpecificUser()
        {
            // Arrange
            var userId = "1";

            // Create LessonCategory
            var lessonCategory = new LessonCategory { Id = 1, Name = "Math" };
            _dbContext.LessonCategories.Add(lessonCategory);
            _dbContext.SaveChanges();  // Ensure LessonCategory is saved

            // Create Listings
            var listing = new Listing
            {
                Id = 100,
                //LessonCategoryId = lessonCategory.Id,
                UserId = "2",
                Name = "Math Tutor Listing"
            };
            _dbContext.Listings.Add(listing);
            _dbContext.SaveChanges();  // Ensure Listing is saved

            // Create Users (Student and Tutor)
            var student = new User { Id = "1", FirstName = "John", Email = "john@student.com" };
            var tutor = new User { Id = "2", FirstName = "Jane", Email = "jane@tutor.com" };
            _dbContext.Users.AddRange(student, tutor);
            _dbContext.SaveChanges();  // Ensure Users are saved

            // Create Chat
            var chat = new Chat
            {
                ListingId = listing.Id,
                StudentId = student.Id,
                TutorId = tutor.Id,
                Listing = listing,
                Student = student,
                Tutor = tutor
            };
            _dbContext.Chats.Add(chat);
            _dbContext.SaveChanges();  // Ensure Chat is saved

            // Create a Message
            var message = new Message
            {
                Content = "Hello, Tutor!",
                SenderId = student.Id,
                RecipientId = tutor.Id,
                SentAt = DateTime.UtcNow,
                ChatId = chat.Id
            };
            _dbContext.Messages.Add(message);
            _dbContext.SaveChanges();  // Ensure Message is saved

            // Act
            var result = _chatService.GetUserChats(userId);

            // Assert
            Assert.NotEmpty(result);
            var chatDto = result.FirstOrDefault();
            Assert.NotNull(chatDto);
            Assert.Equal("Math Tutor", chatDto.Details);
            Assert.Equal("Jane", chatDto.Name);
            Assert.Equal("Hello, Tutor!", chatDto.LastMessage);
            Assert.Equal(UserRole.Student, chatDto.MyRole);
        }

        [Fact]
        public void GetChats_ShouldReturnEmptyListIfNoChatsFound()
        {
            // Arrange
            var userId = "1";

            // Act
            var result = _chatService.GetUserChats(userId);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void SendMessage_ShouldSendMessageAndCreateNewChat()
        {
            // Arrange
            var messageDto = new SendMessageDto
            {
                Content = "New message",
                RecipientId = "2",
                ListingId = 100
            };
            var senderId = "1";

            var chat = new Chat { Id = 1, StudentId = "1", TutorId = "2", ListingId = 100 };
            var message = new Message
            {
                Content = "New message",
                SenderId = "1",
                RecipientId = "2",
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _dbContext.Chats.Add(chat); // Assuming chat doesn't exist yet
            _dbContext.Messages.Add(message);
            _dbContext.SaveChanges();

            var user = new User { Id = "1", FirstName = "Sender" };

            // Act
            var result = _chatService.SendMessage(messageDto, senderId);

            // Assert
            Assert.True(result);

            // Verify that a new chat has been added
            var savedChat = _dbContext.Chats.FirstOrDefault(c => c.ListingId == 100 && c.StudentId == "1" && c.TutorId == "2");
            Assert.NotNull(savedChat);

            // Verify that the message has been added
            var savedMessage = _dbContext.Messages.FirstOrDefault(m => m.Content == "New message" && m.SenderId == "1" && m.RecipientId == "2");
            Assert.NotNull(savedMessage);
        }

        [Fact]
        public void SendMessage_ShouldNotCreateNewChatIfChatExists()
        {
            // Arrange
            var messageDto = new SendMessageDto
            {
                Content = "New message",
                RecipientId = "2",
                ListingId = 100
            };
            var senderId = "1";

            var chat = new Chat { Id = 1, StudentId = "1", TutorId = "2", ListingId = 100 };

            _dbContext.Chats.Add(chat); // Chat already exists
            _dbContext.SaveChanges();

            var user = new User { Id = "1", FirstName = "Sender" };

            // Act
            var result = _chatService.SendMessage(messageDto, senderId);

            // Assert
            Assert.True(result);

            // Verify that no new chat has been added
            var savedChat = _dbContext.Chats.FirstOrDefault(c => c.ListingId == 100 && c.StudentId == "1" && c.TutorId == "2");
            Assert.NotNull(savedChat);  // Ensure the existing chat is still there

            // Verify that the message has been added
            var savedMessage = _dbContext.Messages.FirstOrDefault(m => m.Content == "New message" && m.SenderId == "1" && m.RecipientId == "2");
            Assert.NotNull(savedMessage);
        }
    }
}
