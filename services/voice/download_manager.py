from pathlib import Path
import requests


class DownloadManager:

    def __init__(self):

        self.chunk_size = 8192

    # ==========================================
    # Download File
    # ==========================================

    def download_file(
        self,
        url,
        destination,
        progress_callback=None
    ):

        destination = Path(destination)

        destination.parent.mkdir(
            parents=True,
            exist_ok=True
        )

        response = requests.get(
            url,
            stream=True,
            timeout=30
        )

        response.raise_for_status()

        total = int(
            response.headers.get(
                "content-length",
                0
            )
        )

        downloaded = 0

        with open(destination, "wb") as f:

            for chunk in response.iter_content(
                chunk_size=self.chunk_size
            ):

                if not chunk:
                    continue

                f.write(chunk)

                downloaded += len(chunk)

                if progress_callback:

                    if total > 0:

                        percent = downloaded / total

                    else:

                        percent = 0

                    progress_callback(
                        downloaded,
                        total,
                        percent
                    )

        return destination

    # ==========================================
    # Download Voice
    # ==========================================

    def download_voice(
        self,
        voice,
        progress_callback=None
    ):

        self.download_file(
            voice.model_url,
            voice.model_path,
            progress_callback
        )

        self.download_file(
            voice.config_url,
            voice.config_path,
            progress_callback
        )

        return True

    # ==========================================
    # Delete Voice
    # ==========================================

    def delete_voice(self, voice):

        if (
            voice.model_path
            and voice.model_path.exists()
        ):
            voice.model_path.unlink()

        if (
            voice.config_path
            and voice.config_path.exists()
        ):
            voice.config_path.unlink()

        return True