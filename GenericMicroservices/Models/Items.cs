namespace GenericMicroservices.Models;

// Represents a single item record in the database.
public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}