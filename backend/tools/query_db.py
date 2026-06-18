import sqlite3
import os
p = os.path.join(os.path.dirname(__file__), '..', 'app.db')
print('DB path:', os.path.abspath(p))
if not os.path.exists(p):
    print('DB not found')
    raise SystemExit(0)
conn = sqlite3.connect(p)
cur = conn.cursor()

print('\nUsers:')
for row in cur.execute('SELECT Id, Email, Role FROM Users'):
    print(row)

print('\nSubmissions:')
for row in cur.execute('SELECT Id, Title, Status, CorrespondingUserId FROM Submissions'):
    print(row)

print('\nReviewAssignments:')
for row in cur.execute('SELECT Id, SubmissionId, ReviewerId, AssignedAt FROM ReviewAssignments'):
    print(row)

print('\nReviews:')
for row in cur.execute('SELECT Id, SubmissionId, ReviewerId, Rating, Content FROM Reviews'):
    print(row)

print('\nNotifications:')
for row in cur.execute('SELECT Id, UserId, Message, IsRead FROM Notifications'):
    print(row)

conn.close()
