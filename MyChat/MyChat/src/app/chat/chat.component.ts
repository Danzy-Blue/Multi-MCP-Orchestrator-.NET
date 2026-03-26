import { Component } from '@angular/core';
import { ApiService } from '../services/api.service';

@Component({ selector:'app-chat', templateUrl:'./chat.component.html', styleUrls:['./chat.component.css']})
export class ChatComponent {
  userPrompt:string=''; messages:{sender:string,text:string}[]=[];
  sessionId: string | undefined;

  constructor(private api:ApiService){}

  onTextareaEnter(event: any) {
    const keyboardEvent = event as KeyboardEvent;

    if (keyboardEvent.shiftKey) {
      // Allow an actual newline on Shift+Enter
      return;
    }

    // Send on plain Enter
    keyboardEvent.preventDefault();
    this.sendPrompt();
  }

  sendPrompt(){ 
    if(!this.userPrompt.trim()) return;
    this.messages.push({sender:'user',text:this.userPrompt});
    const msg=this.userPrompt; 
    this.userPrompt='';
    this.api.sendPrompt(msg, this.sessionId).subscribe({
      next:(res)=>{
        this.sessionId = res.session_id; // Save session_id for context
        this.messages.push({sender:'bot',text:res.reply||'No response'});
      },
      error:()=>this.messages.push({sender:'bot',text:'Error calling API'})
    }); 
  }
}
