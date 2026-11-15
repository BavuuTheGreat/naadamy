# Naadamy Privacy Policy

Effective date: 15 November 2026

Naadamy is a game for children and families to learn Mongolian heritage together. This policy explains what data the
Naadamy app and its server collect, why, who else handles it, and how to delete it.

Contact: **naro.tech123@gmail.com**

## Data we collect

**Player profile.** The name (or nickname), age and mascot picture a player chooses, and their game progress: coins,
XP, clothes, lessons, badges, stamps and story progress. We use this to run the game and to save progress so it can
continue on another device.

**Family data.** When a grown-up creates a family, we store the family code, each child's name, age and mascot, and a
salted hash of each child's 3 secret pictures and of the parent PIN. We never store the PIN or the pictures in plain
form.

**Voice recordings.** Families can record short voice clips for their memory book. Clips are stored on our server in
private storage. Only devices signed in to that family can play them. They are never shared publicly and are not sent
to the AI.

**Questions to Basar (AI companion).** When a child types a question to Basar, the text is sent to our server and on to
Anthropic's Claude API to write the answer. Basar is instructed never to ask for personal information and to tell
children to keep such information private. We do not keep a stored history of these chats on our server.

**Email address (optional).** A parent may add an email address to receive a weekly progress report. We send a
confirmation email first and only send reports after the address is confirmed. Players may also sign in with an email
address and password, or with Google. With Google sign-in we keep only Google's account number and the email address.
Passwords are stored only as salted hashes.

**Usernames (optional).** Players who use the online wrestling arena choose a username and password. Other players can
see the username, friend list status, rankings and match results.

**Multiplayer.** In shared rooms, other players in the same room see your player name, character and in-game actions.
Chat in rooms uses preset phrases and emotes only; players cannot type free text to each other.

**Technical data.** Our server sees IP addresses to limit abuse (for example, too many requests per minute). We do not
use advertising, ad tracking or third-party analytics in the app.

## Who else handles data

We do not sell personal data. We use these service providers only to run Naadamy:

- **Anthropic** (Claude API): writes Basar's answers to children's questions.
- **Render**: hosts our server.
- **Supabase**: stores voice recordings in a private bucket, and may host our database.
- **Resend**: sends confirmation and weekly report emails.
- **Google**: only if a player chooses to sign in with Google.

## Children

Naadamy is made for children to use with their families. The parent dashboard, adding children to a family, adding a
parent email and deleting another child's voice clip all require the parent PIN. We collect only the data needed for
the features above. A parent may ask us at any time to see or delete their child's data.

## Keeping and deleting data

We keep data while the account or family is in use.

- **Erase a family:** in the parent dashboard (PIN required), erasing the family deletes every child's profile,
  progress, coins, sign-in tickets, accounts, chat lines and the family's voice recordings from our server. We keep
  only the erased player IDs for 30 days so an old device cannot bring the account back.
- **Delete an arena account:** deletes the username, password, friendships and arena stats.
- **Stop emails:** every report has a stop link. Using it deletes the email address.
- **Anything else:** email us at the contact address above.

Data saved on the device itself is removed by deleting the app.

## Security

We store PINs, passwords, secret pictures and sign-in tokens only as salted hashes. Data is sent over HTTPS. No system
is perfectly secure, but we work to protect your data.

## Changes

If we change this policy, we will update this page and the effective date above.
