from dataclasses import dataclass
from pathlib import Path


@dataclass
class Voice:

    id: str = ""
    display_name: str = ""

    language: str = ""
    region: str = ""
    voice: str = ""

    gender: str = ""
    quality: str = ""
    engine: str = "Piper"

    installed: bool = False

    model_path: Path | None = None
    config_path: Path | None = None

    sample_text: str = (
        "Welcome to Fact Vault. "
        "Did you know sharks have existed longer than trees?"
    )

    @property
    def ready(self) -> bool:
        """
        Returns True if both required Piper files exist.
        """
        return (
            self.model_path is not None
            and self.config_path is not None
            and self.model_path.exists()
            and self.config_path.exists()
        )

    @property
    def filename(self) -> str:
        """
        Returns the model filename.
        """
        return f"{self.id}.onnx"

    @property
    def config_filename(self) -> str:
        """
        Returns the config filename.
        """
        return f"{self.id}.onnx.json"

    def to_dict(self) -> dict:
        """
        Convert the object into a dictionary.
        """

        return {
            "id": self.id,
            "display_name": self.display_name,
            "language": self.language,
            "gender": self.gender,
            "quality": self.quality,
            "engine": self.engine,
            "installed": self.installed,
            "sample_text": self.sample_text,
            "region": self.region,
            "voice": self.voice,
}

    @classmethod
    def from_dict(cls, data: dict):
        """
        Create a Voice object from JSON data.
        """
        return cls(
            id=data.get("id", ""),
            display_name=data.get("display_name", ""),
            language=data.get("language", ""),
            gender=data.get("gender", ""),
            quality=data.get("quality", ""),
            engine=data.get("engine", "Piper"),
            installed=data.get("installed", False),
            sample_text=data.get(
                "sample_text",
                "Welcome to Fact Vault."
            ),
            region=data.get("region", ""),
            voice=data.get("voice", ""),
        )

    def __str__(self) -> str:
        return (
            f"{self.display_name} "
            f"({self.language} • {self.quality})"
        )