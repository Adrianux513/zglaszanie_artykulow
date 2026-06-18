# System zgłaszania artykułów naukowych 

## Co jest w środku
- Backend: ASP.NET Core  (minimal API) + EF Core + SQLite
  - Seedowane konta: admin@example.com/adminpass (role=admin), student@example.com/pass123 (role=author), reviewer1@example.com/reviewerpass1 (role=reviewer), reviewer2@example.com/reviewerpass2 (role=reviewer), reviewer3@example.com/reviewerpass3 (role=reviewer)
  - Endpoints: /api/auth/register, /api/auth/login, /api/submissions (POST, GET, PUT), /api/admin/submissions (GET, admin only), /api/admin/reviewers (GET, admin only), /api/submissions/{id}/assign-reviewer (POST), /api/reviews/assigned (GET), /api/submissions/{id}/reviews (GET, POST), /api/submissions/{id}/decision (POST), /api/notifications (GET, PUT)
- Frontend: React + Vite
  - Login/Register, New Submission, My Submissions, Assigned Reviews, Notifications, Admin view (visible for admin only)
- CORS i JWT obsługiwane (gotowe do uruchomienia lokalnie)

## Uruchomienie
1. Backend
   
   cd backend
   dotnet restore
   dotnet run
   
   Backend nasłuchuje na http://localhost:5000

2. Frontend
   
   cd frontend
   npm install
   npm run dev
   
   Frontend: http://localhost:3000

     

> Jeśli uruchamiasz aplikację po raz pierwszy z nową bazą danych, usuń plik `backend/app.db` przed uruchomieniem backendu, aby nowy schemat recenzji i powiadomień został utworzony.

3. Docker
   docker compose down
   docker compose up --build

## Testowe konta
- admin@example.com / adminpass (admin)
- student@example.com / pass123 (author)
- reviewer1@example.com / reviewerpass1 (reviewer)
- reviewer2@example.com / reviewerpass2 (reviewer)
- reviewer3@example.com / reviewerpass3 (reviewer)


