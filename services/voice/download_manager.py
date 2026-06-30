from pathlib import Path
import requests


class DownloadManager:

    def __init__(self, download_folder):

        self.download_folder = Path(download_folder)

        self.download_folder.mkdir(
            parents=True,
            exist_ok=True
        )

    # ==========================================
    # Download a file
    # ==========================================

    def download(
        self,
        url,
        filename,
        progress_callback=None
    ):

        destination = self.download_folder / filename

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

        with open(destination, "wb") as file:

            for chunk in response.iter_content(
                chunk_size=8192
            ):

                if not chunk:
                    continue

                file.write(chunk)

                downloaded += len(chunk)

                if progress_callback and total:

                    progress_callback(
                        downloaded,
                        total
                    )

        return destination

    # ==========================================
    # Delete
    # ==========================================

    def delete(self, filename):

        file = self.download_folder / filename

        if file.exists():

            file.unlink()

            return True

        return False