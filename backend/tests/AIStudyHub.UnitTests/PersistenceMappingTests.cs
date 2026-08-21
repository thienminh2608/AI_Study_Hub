using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AIStudyHub.UnitTests;

public class PersistenceMappingTests
{
    [Fact]
    public void ChatMessage_MessageContent_Maps_To_Legacy_SnakeCase_Column()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

        var entityType = db.Model.FindEntityType(typeof(ChatMessage));
        var property = entityType?.FindProperty(nameof(ChatMessage.MessageContent));
        var table = StoreObjectIdentifier.Table("chat_messages", schema: null);

        Assert.NotNull(property);
        Assert.Equal("message_content", property.GetColumnName(table));
    }

    [Fact]
    public void ChatMessageCitation_PrimaryKey_Uses_Int64_For_SqlServer_BigInt()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

        var entityType = db.Model.FindEntityType(typeof(ChatMessageCitation));
        var property = entityType?.FindProperty(nameof(ChatMessageCitation.CitationId));

        Assert.NotNull(property);
        Assert.Equal(typeof(long), property.ClrType);
    }
}
