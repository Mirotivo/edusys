# Edusys Project

## How to Clone and Run the Project

### Step 1: Clone the Repository

Clone the repository along with its submodules to ensure all components are properly fetched:
```bash
git clone --recurse-submodules https://github.com/Mirotivo/edusys.git
```

### Step 2: Navigate to the Project Directory

Change into the project directory:
```bash
cd edusys
```

### Step 3: Run the Backend and Frontend Services

#### Option 1: Run Without Docker

1. **Run the Backend**:
   Navigate to the `Backend` submodule directory and run the backend using .NET:
   ```bash
   cd Backend
   dotnet run
   ```

2. **Run the Frontend**:
   Navigate to the `Frontend.Angular` submodule directory, install dependencies, and serve the frontend using Angular CLI:
   ```bash
   cd Frontend.Angular
   npm install
   ng serve
   ```

#### Option 2: Run Using Docker Compose

Start the project using Docker Compose:
1. Build the Docker images:
   ```bash
   docker-compose build
   ```

2. Run the containers:
   ```bash
   docker-compose up -d
   ```

The backend and frontend services should now be running and accessible on their respective ports.

## Documentation

### Project Initialization

1. **Initialize a New Git Repository**:
   ```bash
   git init
   git remote add origin https://github.com/Mirotivo/edusys.git
   ```

2. **Create an Initial Commit**:
   ```bash
   git commit --allow-empty -m "Initial commit"
   ```

3. **Push to Remote Repository**:
   ```bash
   git push -u origin master --force
   ```

4. **Add Submodules**:
   Add the backend and frontend repositories as submodules:
   ```bash
   git submodule add https://github.com/mirotivo/edusys.backend.git Backend
   git submodule add https://github.com/mirotivo/edusys.frontend.git Frontend.Angular
   ```

## Deployment
### AWS ECR for Backend

1. **Create a Repository**:
   ```bash
   aws ecr create-repository --repository-name avancira-backend-container
   ```

2. **Log In to AWS ECR**:
   ```bash
   aws ecr get-login-password --region ap-southeast-2 | docker login --username AWS --password-stdin {repositoryUri}
   ```

3. **Tag and Push the Backend Image**:
   ```bash
   docker tag avancira-backend-container {repositoryUri}
   docker push {repositoryUri}
   ```

4. **Pull and Run the Backend Image**:
   ```bash
   docker pull {repositoryUri}
   docker run -d -p 9000:443 -v /app/Database:/avancira-backend/Database {repositoryUri}
   ```

### AWS ECR for Frontend

1. **Create a Repository**:
   ```bash
   aws ecr create-repository --repository-name avancira-frontend-container
   ```

2. **Log In to AWS ECR**:
   ```bash
   aws ecr get-login-password --region ap-southeast-2 | docker login --username AWS --password-stdin {repositoryUri}
   ```

3. **Tag and Push the Frontend Image**:
   ```bash
   docker tag avancira-frontend-container {repositoryUri}
   docker push {repositoryUri}
   ```

4. **Pull and Run the Frontend Image**:
   ```bash
   docker pull {repositoryUri}
   docker run -d -p 8000:443 -p 8080:80 {repositoryUri}
   ```

---

Feel free to modify the `{repositoryUri}` placeholder with the actual AWS ECR repository URI.
