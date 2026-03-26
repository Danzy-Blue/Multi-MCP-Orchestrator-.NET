import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({providedIn:'root'})
export class ApiService{
 private apiUrl =
  (window as Window & { __myChatConfig?: { apiUrl?: string } }).__myChatConfig?.apiUrl
  ?? 'http://localhost:8888/chat';

 constructor(private http:HttpClient){}

 sendPrompt(prompt: string, sessionId?: string): Observable<any> {
  const body: any = { message: prompt };
  if (sessionId) {
    body.session_id = sessionId;
  }
  return this.http.post(this.apiUrl, body);
 }
}
