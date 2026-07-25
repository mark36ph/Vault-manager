import subprocess
import sys
import time
from watchfiles import watch


APP_FILE = "main.py"


def start_app():

    return subprocess.Popen(
        [
            sys.executable,
            APP_FILE
        ]
    )


def stop_app(process):

    if process is None:
        return

    if process.poll() is not None:
        return

    print("Closing old app...")

    process.terminate()

    try:

        process.wait(
            timeout=3
        )

    except subprocess.TimeoutExpired:

        process.kill()


if __name__ == "__main__":

    print("Starting Fact Vault Manager...")
    print("Watching for .py changes...")
    print("Press CTRL + C to stop.")

    app_process = start_app()

    try:

        for changes in watch(
            ".",
            watch_filter=lambda change, path: path.endswith(".py")
        ):

            # Do not restart because dev_run.py itself changed
            changed_files = [
                path
                for change, path in changes
            ]

            if all(
                path.endswith("dev_run.py")
                for path in changed_files
            ):

                continue

            print("Code changed. Restarting app...")

            stop_app(
                app_process
            )

            time.sleep(
                0.5
            )

            app_process = start_app()

    except KeyboardInterrupt:

        print("Stopping watcher...")

        stop_app(
            app_process
        )