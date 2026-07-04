import sqlite3

DB = "data/factvault.db"

categories = [
    "History",
    "Science",
    "Space",
    "Geography",
    "Technology",
    "Nature",
    "Human Body",
    "Animals",
    "Weather",
    "Mysteries",
    "Food",
    "Money",
    "World Records",
    "Survival",
    "Ancient Civilizations",
    "Inventions",
    "Transport",
    "Ocean",
    "Crime",
    "Weird Facts"
]

conn = sqlite3.connect(DB)
cur = conn.cursor()

# Remove existing categories
cur.execute("DELETE FROM categories")

# Add new categories
for category in categories:
    cur.execute(
        "INSERT INTO categories(name) VALUES(?)",
        (category,)
    )

conn.commit()
conn.close()

print("Categories updated successfully.")