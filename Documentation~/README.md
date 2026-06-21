# VelNet Documentation

## Setup

1. Create or activate a pip environment
   - Create:
      - `python -m venv env`
   - Activate:
      - PowerShell: `.\env\Scripts\Activate.ps1`
      - CMD: `.\env\Scripts\Activate.bat`
2. Install requirements:
   - `pip install -r requirements.txt`
3. Run:
   - `mkdocs serve`
4. Build and Deploy
   - Building and deploying happens automatically using a GitHub Action on push. If you want to build manually, use this command:
     - `mkdocs build`
   - For more information, visit these docs pages: https://squidfunk.github.io/mkdocs-material/getting-started/